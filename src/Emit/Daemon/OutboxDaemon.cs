namespace Emit.Daemon;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.Daemon;
using Emit.Configuration;
using Emit.Metrics;
using Emit.Models;
using Emit.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Daemon agent that processes outbox entries using leader-driven assignment coordination.
/// </summary>
internal sealed class OutboxDaemon : IDaemonAgent
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly Dictionary<string, IOutboxProvider> providers;
    private readonly OutboxObserverInvoker observerInvoker;
    private readonly OutboxMetrics outboxMetrics;
    private readonly OutboxOptions outboxOptions;
    private readonly ILogger<OutboxDaemon> logger;

    private Task? processingTask;
    private CancellationTokenSource? stoppingCts;

    /// <inheritdoc />
    public string DaemonId => "emit:outbox";

    public OutboxDaemon(
        IServiceScopeFactory scopeFactory,
        IEnumerable<IOutboxProvider> providers,
        OutboxObserverInvoker observerInvoker,
        OutboxMetrics outboxMetrics,
        IOptions<OutboxOptions> outboxOptions,
        ILogger<OutboxDaemon> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(observerInvoker);
        ArgumentNullException.ThrowIfNull(outboxMetrics);
        ArgumentNullException.ThrowIfNull(outboxOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.scopeFactory = scopeFactory;
        this.providers = providers.ToDictionary(p => p.SystemId);
        this.observerInvoker = observerInvoker;
        this.outboxMetrics = outboxMetrics;
        this.outboxOptions = outboxOptions.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken assignmentToken)
    {
        stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(assignmentToken);
        processingTask = ProcessingLoopAsync(stoppingCts.Token);

        logger.LogInformation("Outbox daemon started");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (stoppingCts is not null)
        {
            await stoppingCts.CancelAsync().ConfigureAwait(false);
        }

        if (processingTask is not null)
        {
            try
            {
                await processingTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        stoppingCts?.Dispose();
        stoppingCts = null;
        processingTask = null;

        logger.LogInformation("Outbox daemon stopped");
    }

    private async Task ProcessingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var start = Stopwatch.GetTimestamp();
            var dispatched = 0;
            try
            {
                dispatched = await DispatchBatchAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                outboxMetrics.RecordWorkerError();
                logger.LogError(ex, "Error in outbox processing loop");
            }

            // A full batch that was fully dispatched means the outbox almost
            // certainly holds more work. Skip the poll delay and drain the next
            // batch immediately. A short batch (queue drained) or a partially
            // dispatched batch (a head entry failed and remains, by ordering
            // guarantee) falls through to the normal wait, which doubles as the
            // retry cadence for that stuck entry.
            if (dispatched >= outboxOptions.BatchSize)
            {
                continue;
            }

            var remaining = outboxOptions.PollingInterval - Stopwatch.GetElapsedTime(start);
            if (remaining > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Dispatches a single batch and returns the number of entries successfully
    /// processed and deleted. A return value equal to the configured batch size
    /// signals that the outbox was full and fully drained this cycle.
    /// </summary>
    private async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();

        var entries = await outboxRepository.GetBatchAsync(
            outboxOptions.BatchSize,
            cancellationToken).ConfigureAwait(false);

        if (entries is [])
        {
            outboxMetrics.RecordPollCycle(hasEntries: false);
            return 0;
        }

        outboxMetrics.RecordPollCycle(hasEntries: true);
        outboxMetrics.RecordBatchEntries(entries.Count);

        var partitionedEntries = entries.GroupBy(e => e.GroupKey);

        // Each group is processed sequentially to preserve ordering; distinct groups are
        // dispatched concurrently up to MaxConcurrentGroups. Awaiting completion here forms a
        // barrier between batches, which preserves ordering for a group key that spans batches.
        var dispatched = 0;
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = outboxOptions.MaxConcurrentGroups,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(partitionedEntries, parallelOptions, async (group, ct) =>
        {
            try
            {
                var groupEntries = group.OrderBy(e => e.Sequence).ToList();
                var count = await ProcessGroupAsync(outboxRepository, group.Key, groupEntries, ct).ConfigureAwait(false);
                Interlocked.Add(ref dispatched, count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unhandled error processing group {GroupKey}", group.Key);
            }
        }).ConfigureAwait(false);

        return dispatched;
    }

    /// <summary>
    /// Processes a group's entries in sequence order, stopping at the first
    /// failure to preserve ordering, and returns the number successfully
    /// processed and deleted.
    /// </summary>
    private async Task<int> ProcessGroupAsync(
        IOutboxRepository outboxRepository,
        string groupKey,
        List<OutboxEntry> entries,
        CancellationToken cancellationToken)
    {
        var dispatched = 0;

        foreach (var entry in entries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var success = await ProcessEntryAsync(outboxRepository, entry, cancellationToken).ConfigureAwait(false);

            if (!success)
            {
                logger.LogWarning(
                    "Stopping group {GroupKey} processing due to failure at sequence {Sequence}",
                    groupKey, entry.Sequence);
                break;
            }

            dispatched++;
        }

        return dispatched;
    }

    private async Task<bool> ProcessEntryAsync(IOutboxRepository outboxRepository, OutboxEntry entry, CancellationToken cancellationToken)
    {
        if (!providers.TryGetValue(entry.SystemId, out var provider))
        {
            logger.LogError(
                "No provider found for SystemId '{SystemId}' on entry {EntryId}. " +
                "Entry will be retried on the next poll cycle.",
                entry.SystemId, entry.Id);

            return false;
        }

        var startTicks = Stopwatch.GetTimestamp();

        try
        {
            await observerInvoker.OnProcessingAsync(entry, cancellationToken).ConfigureAwait(false);

            await provider.ProcessAsync(entry, cancellationToken).ConfigureAwait(false);

            await outboxRepository.DeleteAsync(entry.Id, cancellationToken).ConfigureAwait(false);

            var elapsed = Stopwatch.GetElapsedTime(startTicks).TotalSeconds;
            outboxMetrics.RecordProcessingDuration(elapsed, entry.SystemId, "success");
            outboxMetrics.RecordProcessingCompleted(entry.SystemId, "success");
            outboxMetrics.RecordCriticalTime(
                (DateTime.UtcNow - entry.EnqueuedAt).TotalSeconds, entry.SystemId);

            await observerInvoker.OnProcessedAsync(entry, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTicks).TotalSeconds;
            outboxMetrics.RecordProcessingDuration(elapsed, entry.SystemId, "error");
            outboxMetrics.RecordProcessingCompleted(entry.SystemId, "error");

            await observerInvoker.OnProcessErrorAsync(entry, ex, cancellationToken).ConfigureAwait(false);

            logger.LogError(
                ex,
                "Failed to process entry {EntryId} from group {GroupKey} sequence {Sequence}. " +
                "Entry will be retried on the next poll cycle.",
                entry.Id, entry.GroupKey, entry.Sequence);

            return false;
        }
    }
}

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
            try
            {
                await DispatchBatchAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();

        var entries = await outboxRepository.GetBatchAsync(
            outboxOptions.BatchSize,
            cancellationToken).ConfigureAwait(false);

        if (entries is [])
        {
            outboxMetrics.RecordPollCycle(hasEntries: false);
            return;
        }

        outboxMetrics.RecordPollCycle(hasEntries: true);
        outboxMetrics.RecordBatchEntries(entries.Count);

        var partitionedEntries = entries.GroupBy(e => e.GroupKey);
        var tasks = new List<Task>();

        foreach (var group in partitionedEntries)
        {
            var groupKey = group.Key;
            var groupEntries = group.OrderBy(e => e.Sequence).ToList();

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await ProcessGroupAsync(outboxRepository, groupKey, groupEntries, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Unhandled error processing group {GroupKey}", groupKey);
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task ProcessGroupAsync(
        IOutboxRepository outboxRepository,
        string groupKey,
        List<OutboxEntry> entries,
        CancellationToken cancellationToken)
    {
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
        }
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

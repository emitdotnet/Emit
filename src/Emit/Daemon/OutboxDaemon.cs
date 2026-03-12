namespace Emit.Daemon;

using System.Collections.Concurrent;
using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.Daemon;
using Emit.Configuration;
using Emit.Metrics;
using Emit.Models;
using Emit.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Daemon agent that processes outbox entries using leader-driven assignment coordination.
/// </summary>
internal sealed class OutboxDaemon : IDaemonAgent
{
    private readonly IOutboxRepository outboxRepository;
    private readonly Dictionary<string, IOutboxProvider> providers;
    private readonly OutboxObserverInvoker observerInvoker;
    private readonly OutboxMetrics outboxMetrics;
    private readonly OutboxOptions outboxOptions;
    private readonly ILogger<OutboxDaemon> logger;

    private readonly ConcurrentDictionary<string, byte> activeGroups = new();

    private Task? processingTask;
    private CancellationTokenSource? stoppingCts;

    /// <inheritdoc />
    public string DaemonId => "emit:outbox";

    public OutboxDaemon(
        IOutboxRepository outboxRepository,
        IEnumerable<IOutboxProvider> providers,
        OutboxObserverInvoker observerInvoker,
        OutboxMetrics outboxMetrics,
        IOptions<OutboxOptions> outboxOptions,
        ILogger<OutboxDaemon> logger)
    {
        ArgumentNullException.ThrowIfNull(outboxRepository);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(observerInvoker);
        ArgumentNullException.ThrowIfNull(outboxMetrics);
        ArgumentNullException.ThrowIfNull(outboxOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.outboxRepository = outboxRepository;
        this.providers = providers.ToDictionary(p => p.SystemId);
        this.observerInvoker = observerInvoker;
        this.outboxMetrics = outboxMetrics;
        this.outboxOptions = outboxOptions.Value;
        this.logger = logger;

        outboxMetrics.RegisterActiveGroupsCallback(() => activeGroups.Count);
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
            try
            {
                var processedFull = await DispatchGroupsAsync(cancellationToken).ConfigureAwait(false);

                if (!processedFull)
                {
                    await Task.Delay(outboxOptions.PollingInterval, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                outboxMetrics.RecordWorkerError();
                logger.LogError(ex, "Error in outbox processing loop");
                await Task.Delay(outboxOptions.PollingInterval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> DispatchGroupsAsync(CancellationToken cancellationToken)
    {
        var groupHeads = await outboxRepository.GetGroupHeadsAsync(
            outboxOptions.MaxGroupsPerCycle,
            cancellationToken).ConfigureAwait(false);

        if (groupHeads is [])
        {
            outboxMetrics.RecordPollCycle(hasEntries: false);
            return false;
        }

        List<string> eligibleGroups = [];
        foreach (var head in groupHeads)
        {
            if (activeGroups.ContainsKey(head.GroupKey))
            {
                continue;
            }

            eligibleGroups.Add(head.GroupKey);
        }

        if (eligibleGroups is [])
        {
            outboxMetrics.RecordPollCycle(hasEntries: false);
            return false;
        }

        var entries = await outboxRepository.GetBatchAsync(
            eligibleGroups,
            outboxOptions.BatchSize,
            cancellationToken).ConfigureAwait(false);

        if (entries is [])
        {
            outboxMetrics.RecordPollCycle(hasEntries: false);
            return false;
        }

        outboxMetrics.RecordPollCycle(hasEntries: true);
        outboxMetrics.RecordBatchEntries(entries.Count);

        logger.LogInformation(
            "Dispatching batch of {EntryCount} entries from {GroupCount} groups",
            entries.Count, eligibleGroups.Count);

        var partitionedEntries = entries.GroupBy(e => e.GroupKey);

        foreach (var group in partitionedEntries)
        {
            var groupKey = group.Key;

            if (!activeGroups.TryAdd(groupKey, 0))
            {
                continue;
            }

            var groupEntries = group.OrderBy(e => e.Sequence).ToList();

            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessGroupAsync(groupKey, groupEntries, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Unhandled error processing group {GroupKey}", groupKey);
                }
                finally
                {
                    activeGroups.TryRemove(groupKey, out _);
                }
            }, cancellationToken);
        }

        return entries.Count >= outboxOptions.BatchSize;
    }

    private async Task ProcessGroupAsync(
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

            var success = await ProcessEntryAsync(entry, cancellationToken).ConfigureAwait(false);

            if (!success)
            {
                logger.LogWarning(
                    "Stopping group {GroupKey} processing due to failure at sequence {Sequence}",
                    groupKey, entry.Sequence);
                break;
            }
        }
    }

    private async Task<bool> ProcessEntryAsync(OutboxEntry entry, CancellationToken cancellationToken)
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

namespace Emit.Daemon;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Emit.Abstractions;
using Emit.Abstractions.Daemon;
using Emit.Configuration;
using Emit.LeaderElection;
using Emit.Metrics;
using Emit.Models;
using Emit.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Daemon agent that processes outbox entries using leader-driven assignment coordination.
/// </summary>
/// <remarks>
/// Each batch is sent through one delivery per provider, which makes its entries visible together, or none of
/// them. A batch that cannot be delivered is read again on the next poll. A provider that can fence earlier
/// writers does it when this run's first delivery begins. A run reads and sends a batch only while it holds its
/// assignment. A run that cannot begin a delivery, or whose provider could not reach its message broker, fails, so
/// a node that cannot reach it gives the daemon up once its runs keep failing.
/// </remarks>
internal sealed class OutboxDaemon : IDaemonAgent
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly Dictionary<string, IOutboxProvider> providers;
    private readonly OutboxObserverInvoker observerInvoker;
    private readonly OutboxMetrics outboxMetrics;
    private readonly OutboxOptions outboxOptions;
    private readonly TimeSpan drainTimeout;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<OutboxDaemon> logger;
    private readonly string serviceName;

    /// <inheritdoc />
    public string DaemonId => "emit:outbox";

    public OutboxDaemon(
        IServiceScopeFactory scopeFactory,
        IEnumerable<IOutboxProvider> providers,
        OutboxObserverInvoker observerInvoker,
        OutboxMetrics outboxMetrics,
        IOptions<OutboxOptions> outboxOptions,
        IOptions<DaemonOptions> daemonOptions,
        INodeIdentity nodeIdentity,
        TimeProvider timeProvider,
        ILogger<OutboxDaemon> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(observerInvoker);
        ArgumentNullException.ThrowIfNull(outboxMetrics);
        ArgumentNullException.ThrowIfNull(outboxOptions);
        ArgumentNullException.ThrowIfNull(daemonOptions);
        ArgumentNullException.ThrowIfNull(nodeIdentity);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.scopeFactory = scopeFactory;
        this.providers = providers.ToDictionary(p => p.SystemId);
        this.observerInvoker = observerInvoker;
        this.outboxMetrics = outboxMetrics;
        this.outboxOptions = outboxOptions.Value;
        drainTimeout = daemonOptions.Value.DrainTimeout;
        this.timeProvider = timeProvider;
        this.logger = logger;
        serviceName = nodeIdentity.ServiceName;
    }

    /// <inheritdoc />
    public async Task RunAsync(DaemonRunContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        logger.LogInformation("Outbox daemon started");

        // Deliveries and deletes may finish after the stop, but only within the drain timeout of it: the options
        // validation keeps that window inside the time this node stays registered
        var run = new OutboxDeliveryContext(context.FencingKey, context.Generation);
        using var drainWindow = new CancellationTokenSource(Timeout.InfiniteTimeSpan, timeProvider);
        using (context.AssignmentToken.Register(
            static state =>
            {
                var (window, timeout) = ((CancellationTokenSource, TimeSpan))state!;
                window.CancelAfter(timeout);
            },
            (drainWindow, drainTimeout)))
        {
            await ProcessingLoopAsync(context, run, context.AssignmentToken, drainWindow.Token).ConfigureAwait(false);
        }

        logger.LogInformation("Outbox daemon stopped");
    }

    private async Task ProcessingLoopAsync(DaemonRunContext daemonRun, OutboxDeliveryContext run, CancellationToken cancellationToken, CancellationToken drainToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var start = timeProvider.GetTimestamp();
            var dispatched = 0;
            try
            {
                dispatched = await DispatchBatchAsync(daemonRun, run, cancellationToken, drainToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (CannotDeliverFromThisNodeException ex)
            {
                // Another node may reach the provider: the coordinator moves the daemon once its runs keep failing
                outboxMetrics.RecordWorkerError();
                ExceptionDispatchInfo.Throw(ex.InnerException!);
            }
            catch (Exception ex)
            {
                outboxMetrics.RecordWorkerError();
                logger.LogError(ex, "Error in outbox processing loop");
            }

            // A full batch that was delivered means the outbox almost certainly holds more work: the next batch is
            // read at once. A short batch, or one that could not be delivered, waits for the poll interval, which is
            // also how often a batch that keeps failing is retried.
            if (dispatched >= outboxOptions.BatchSize)
            {
                continue;
            }

            var remaining = outboxOptions.PollingInterval - timeProvider.GetElapsedTime(start);
            if (remaining > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(remaining, timeProvider, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Delivers a single batch and returns the number of entries delivered and deleted. A return value equal
    /// to the configured batch size signals that the outbox was full and fully drained this cycle.
    /// </summary>
    private async Task<int> DispatchBatchAsync(DaemonRunContext daemonRun, OutboxDeliveryContext run, CancellationToken cancellationToken, CancellationToken drainToken)
    {
        // A run that no longer holds its assignment, even before the timers that stop it have run, reads nothing:
        // the leader may already have given the outbox to another node
        if (!daemonRun.HoldsAssignment)
        {
            return 0;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();

        var entries = await outboxRepository.GetBatchAsync(
            serviceName,
            providers.Keys,
            outboxOptions.BatchSize,
            cancellationToken).ConfigureAwait(false);

        if (entries is [])
        {
            outboxMetrics.RecordPollCycle(hasEntries: false);
            return 0;
        }

        outboxMetrics.RecordPollCycle(hasEntries: true);
        outboxMetrics.RecordBatchEntries(entries.Count);

        var startedAt = Stopwatch.GetTimestamp();
        var delivered = await DeliverAsync(daemonRun, run, entries, startedAt, cancellationToken, drainToken).ConfigureAwait(false);
        return await DeleteDeliveredAsync(outboxRepository, delivered, startedAt, drainToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Hands the batch to one delivery per provider and completes each, returning the entries of the deliveries that
    /// completed. A delivery is all or nothing: when any of its entries cannot be delivered, none is, and its entries
    /// are read again on the next poll. Completing the batch before the next one is read keeps the order of a group
    /// that spans batches.
    /// </summary>
    private async Task<IReadOnlyList<OutboxEntry>> DeliverAsync(
        DaemonRunContext daemonRun,
        OutboxDeliveryContext run,
        IReadOnlyList<OutboxEntry> entries,
        long startedAt,
        CancellationToken cancellationToken,
        CancellationToken drainToken)
    {
        var deliveries = new ConcurrentDictionary<string, Lazy<Task<IOutboxDelivery>>>(StringComparer.Ordinal);
        try
        {
            try
            {
                if (!await HandOverAsync(daemonRun, run, entries, deliveries, cancellationToken).ConfigureAwait(false))
                {
                    // Stopped before the whole batch was handed over: none of it is delivered, and the run that
                    // delivers next sends all of it
                    return [];
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return [];
            }
            catch (Exception ex) when (ex is not CannotDeliverFromThisNodeException)
            {
                logger.LogError(ex, "An entry of the batch could not be handed over; the batch will be delivered again");
                await ReportFailedAsync(entries, startedAt, ex, drainToken).ConfigureAwait(false);
                return [];
            }

            // Completing may finish after the stop, within the drain timeout: what was sent is then not sent again
            List<OutboxEntry> delivered = [];
            foreach (var (systemId, delivery) in deliveries)
            {
                List<OutboxEntry> ofProvider = [.. entries.Where(e => e.SystemId == systemId)];
                try
                {
                    await (await delivery.Value.ConfigureAwait(false)).CompleteAsync(drainToken).ConfigureAwait(false);
                    delivered.AddRange(ofProvider);
                }
                catch (MessageBrokerUnreachableException ex) when (!drainToken.IsCancellationRequested)
                {
                    await ReportFailedAsync(ofProvider, startedAt, ex, drainToken).ConfigureAwait(false);
                    throw new CannotDeliverFromThisNodeException(ex);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "The delivery of provider {SystemId} failed; its {Count} entries will be delivered again", systemId, ofProvider.Count);
                    await ReportFailedAsync(ofProvider, startedAt, ex, drainToken).ConfigureAwait(false);
                }
            }

            return delivered;
        }
        finally
        {
            // A delivery disposed without completing delivers none of its entries
            foreach (var delivery in deliveries.Values.Where(d => d.IsValueCreated && d.Value.IsCompletedSuccessfully))
            {
                await delivery.Value.Result.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Hands every entry of the batch to its provider's delivery, beginning it on first use: each group's entries
    /// one after the other in order, and distinct groups concurrently. Returns <c>false</c> when the run had to stop
    /// before the whole batch was handed over.
    /// </summary>
    private async Task<bool> HandOverAsync(
        DaemonRunContext daemonRun,
        OutboxDeliveryContext run,
        IReadOnlyList<OutboxEntry> entries,
        ConcurrentDictionary<string, Lazy<Task<IOutboxDelivery>>> deliveries,
        CancellationToken cancellationToken)
    {
        var stopped = false;
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = outboxOptions.MaxConcurrentGroups };
        await Parallel.ForEachAsync(entries.GroupBy(e => e.GroupKey), parallelOptions, async (group, _) =>
        {
            foreach (var entry in group.OrderBy(e => e.Sequence))
            {
                if (cancellationToken.IsCancellationRequested || !daemonRun.HoldsAssignment)
                {
                    stopped = true;
                    return;
                }

                // The batch only holds entries of the node's providers
                var provider = providers[entry.SystemId];
                await observerInvoker.OnProcessingAsync(entry, cancellationToken).ConfigureAwait(false);

                IOutboxDelivery delivery;
                try
                {
                    delivery = await deliveries
                        .GetOrAdd(entry.SystemId, _ => new Lazy<Task<IOutboxDelivery>>(() => provider.BeginDeliveryAsync(run, cancellationToken)))
                        .Value.ConfigureAwait(false);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new CannotDeliverFromThisNodeException(ex);
                }

                await delivery.SendAsync(entry, cancellationToken).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        return !stopped;
    }

    /// <summary>
    /// Deletes the delivered entries in one database call and reports them processed, returning how many there
    /// were. Once delivered, the entries are deleted even if the daemon is being stopped, within the drain timeout,
    /// so the node that takes the daemon over does not deliver them again.
    /// </summary>
    private async Task<int> DeleteDeliveredAsync(
        IOutboxRepository outboxRepository,
        IReadOnlyList<OutboxEntry> delivered,
        long startedAt,
        CancellationToken drainToken)
    {
        if (delivered is [])
        {
            return 0;
        }

        try
        {
            await outboxRepository.DeleteAsync([.. delivered.Select(e => e.Id!)], drainToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete {Count} delivered entries; they will be delivered again", delivered.Count);
            return 0;
        }

        var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
        foreach (var entry in delivered)
        {
            outboxMetrics.RecordProcessingDuration(elapsed, entry.SystemId, "success");
            outboxMetrics.RecordProcessingCompleted(entry.SystemId, "success");
            outboxMetrics.RecordCriticalTime((DateTime.UtcNow - entry.EnqueuedAt).TotalSeconds, entry.SystemId);
            await observerInvoker.OnProcessedAsync(entry, drainToken).ConfigureAwait(false);
        }

        return delivered.Count;
    }

    private async Task ReportFailedAsync(IEnumerable<OutboxEntry> failed, long startedAt, Exception exception, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
        foreach (var entry in failed)
        {
            outboxMetrics.RecordProcessingDuration(elapsed, entry.SystemId, "error");
            outboxMetrics.RecordProcessingCompleted(entry.SystemId, "error");
            await observerInvoker.OnProcessErrorAsync(entry, exception, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A provider could not begin a delivery, or could not reach its message broker: this node cannot deliver
    /// through it, though another node may.
    /// </summary>
    private sealed class CannotDeliverFromThisNodeException(Exception innerException)
        : Exception("The provider cannot deliver from this node.", innerException);
}

namespace Emit.Kafka.Consumer;

using Emit.Consumer;
using Emit.Kafka.Metrics;
using Emit.Kafka.Observability;
using Microsoft.Extensions.Logging;

/// <summary>
/// The result of creating a worker pool: the worker instances and their running tasks.
/// </summary>
internal sealed class WorkerPool<TKey, TValue>(
    ConsumerWorker<TKey, TValue>[] workers,
    Task[] tasks)
{
    internal ConsumerWorker<TKey, TValue>[] Workers { get; } = workers;
    internal Task[] Tasks { get; } = tasks;
}

/// <summary>
/// Manages a pool of consumer workers: creation, health monitoring, fault detection,
/// and graceful shutdown. Extracted from <see cref="ConsumerGroupWorker{TKey,TValue}"/>
/// to encapsulate pool lifecycle state and enable independent testing of the monitor logic.
/// </summary>
internal sealed class WorkerPoolSupervisor<TKey, TValue>
{
    private readonly ConsumerGroupRegistration<TKey, TValue> registration;
    private readonly Func<OffsetManager, CancellationToken, WorkerPool<TKey, TValue>> workerFactory;
    private readonly KafkaMetrics kafkaMetrics;
    private readonly KafkaConsumerObserverInvoker observerInvoker;
    private readonly ILogger logger;
    private readonly Action<int>? onUnhandled;

    private ConsumerWorker<TKey, TValue>[]? workers;
    private Task[]? workerTasks;
    private IDistributionStrategy? distributionStrategy;
    private CancellationTokenSource? poolCts;
    private CancellationTokenSource? monitorCts;
    private Task? monitorTask;
    private CancellationToken executionToken;
    private volatile bool isFaulted;

    internal WorkerPoolSupervisor(
        ConsumerGroupRegistration<TKey, TValue> registration,
        Func<OffsetManager, CancellationToken, WorkerPool<TKey, TValue>> workerFactory,
        KafkaMetrics kafkaMetrics,
        KafkaConsumerObserverInvoker observerInvoker,
        ILogger logger,
        Action<int>? onUnhandled = null)
    {
        this.registration = registration;
        this.workerFactory = workerFactory;
        this.kafkaMetrics = kafkaMetrics;
        this.observerInvoker = observerInvoker;
        this.logger = logger;
        this.onUnhandled = onUnhandled;
    }

    /// <summary>
    /// Whether the monitor has detected a worker fault requiring a pool restart.
    /// </summary>
    internal bool IsFaulted => isFaulted;

    /// <summary>
    /// Whether a worker pool is running.
    /// </summary>
    internal bool IsRunning => workers is not null;

    /// <summary>
    /// The current worker instances, or null if the pool is not running.
    /// </summary>
    internal ConsumerWorker<TKey, TValue>[]? Workers => workers;

    /// <summary>
    /// The current message distribution strategy, or null if the pool is not running.
    /// </summary>
    internal IDistributionStrategy? DistributionStrategy => distributionStrategy;

    /// <summary>
    /// Creates the worker pool and starts the health monitor.
    /// </summary>
    internal void Start(OffsetManager offsetManager, CancellationToken executionToken)
    {
        this.executionToken = executionToken;

        distributionStrategy = registration.WorkerDistribution switch
        {
            WorkerDistribution.RoundRobin => new RoundRobinStrategy(registration.WorkerCount),
            _ => new ByKeyHashStrategy(registration.WorkerCount),
        };

        // The pool's workers stop with the host, or when the pool is stopped and their stop timeout is over
        poolCts = CancellationTokenSource.CreateLinkedTokenSource(executionToken);
        var pool = workerFactory(offsetManager, poolCts.Token);
        workers = pool.Workers;
        workerTasks = pool.Tasks;

        logger.LogInformation(
            "Spawned {WorkerCount} workers using {Distribution} distribution for group '{GroupId}'",
            registration.WorkerCount, registration.WorkerDistribution, registration.GroupId);

        monitorCts = CancellationTokenSource.CreateLinkedTokenSource(executionToken);
        monitorTask = RunMonitorAsync(monitorCts.Token);
    }

    /// <summary>
    /// Stops the health monitor and the worker pool. The workers get <c>WorkerStopTimeout</c> to finish the messages
    /// already handed to them, unless <paramref name="cancelAtOnce"/> or the host is stopping, which has cancelled them
    /// already; their handlers are then cancelled, and given
    /// <c>WorkerStopTimeout</c> again to stop. A stopped worker takes no further message, so no message of a revoked
    /// partition is handled after this returns, but for one whose handler did not stop when cancelled: that one is
    /// logged and left running. Waiting for it longer would hold the partitions only until the group reassigns them
    /// anyway. Does not flush offsets: the caller commits them after stopping the pool.
    /// </summary>
    /// <param name="cancelAtOnce">
    /// Cancels the handlers without waiting, for partitions already owned by another consumer.
    /// </param>
    internal async Task StopAsync(bool cancelAtOnce = false)
    {
        // Cancel the monitor first so it doesn't signal a fault during shutdown.
        monitorCts?.Cancel();
        if (monitorTask is not null)
        {
            try { await monitorTask.ConfigureAwait(false); }
            catch { /* Monitor may have already exited after signaling a fault */ }
        }

        monitorCts?.Dispose();
        monitorCts = null;
        monitorTask = null;

        // Complete worker channels and await drain.
        if (workers is not null)
        {
            foreach (var worker in workers)
            {
                worker.Complete();
            }
        }

        if (workerTasks is not null)
        {
            var allWorkers = Task.WhenAll(workerTasks);
            if (cancelAtOnce || !await EndsWithinStopTimeoutAsync(allWorkers).ConfigureAwait(false))
            {
                if (!cancelAtOnce)
                {
                    logger.LogWarning(
                        "Workers of group '{GroupId}' did not finish within {Timeout}; cancelling their handlers",
                        registration.GroupId, registration.WorkerStopTimeout);
                }

                // The cancellation callbacks are not awaited: a handler's could block, or throw, and the wait for the
                // handlers to stop is bounded below
                _ = poolCts!.CancelAsync().ContinueWith(
                    cancelling => logger.LogError(
                        cancelling.Exception,
                        "A cancellation callback of a handler of group '{GroupId}' threw",
                        registration.GroupId),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                if (!await EndsWithinStopTimeoutAsync(allWorkers).ConfigureAwait(false))
                {
                    logger.LogError(
                        "A handler of group '{GroupId}' did not stop within {Timeout} of being cancelled and is left running; it may still be handling a message once its partition is handed over. Handlers must observe their cancellation token",
                        registration.GroupId, registration.WorkerStopTimeout);
                }
            }
        }

        // The messages a worker left in its queue were never handled. A worker left running in a handler takes no
        // further message once cancelled, so its queue is read here alone
        if (workers is not null)
        {
            var unhandled = workers.Sum(worker => worker.DiscardUnread());
            if (unhandled > 0)
            {
                onUnhandled?.Invoke(unhandled);
            }
        }

        // A handler left running may still observe the token: the source goes once every worker has ended
        if (poolCts is not null)
        {
            var stoppedPool = poolCts;
            _ = Task.WhenAll(workerTasks ?? []).ContinueWith(
                _ => stoppedPool.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        poolCts = null;
        workers = null;
        workerTasks = null;
        distributionStrategy = null;
        isFaulted = false;
    }

    /// <summary>
    /// Returns whether the workers ended within the stop timeout, a fault being an end.
    /// </summary>
    private async Task<bool> EndsWithinStopTimeoutAsync(Task allWorkers)
    {
        try
        {
            await allWorkers.WaitAsync(registration.WorkerStopTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during worker pool drain for group '{GroupId}'", registration.GroupId);
        }

        return true;
    }

    /// <summary>
    /// Core monitor logic: waits for any worker task to complete, then determines whether
    /// it was an intentional shutdown or an unexpected fault.
    /// </summary>
    /// <param name="workerTasks">The worker tasks to monitor.</param>
    /// <param name="executionToken">The direct application stop token (not a linked derivative).</param>
    /// <param name="stopToken">Token cancelled to stop the monitor (linked to executionToken, also cancelled on rebalance).</param>
    /// <returns>
    /// The completed task and its index if a fault was detected;
    /// <c>null</c> if the monitor was stopped intentionally (shutdown or rebalance).
    /// </returns>
    internal static async Task<(Task CompletedTask, int WorkerIndex)?> WaitForWorkerFaultAsync(
        Task[] workerTasks,
        CancellationToken executionToken,
        CancellationToken stopToken)
    {
        try
        {
            var completed = await Task.WhenAny(workerTasks)
                .WaitAsync(stopToken).ConfigureAwait(false);

            // The workers' token is linked to executionToken, as stopToken is, and either may lag behind it due to
            // callback propagation. If the application is shutting down, this worker completed because of
            // cancellation, not a fault.
            if (executionToken.IsCancellationRequested)
            {
                return null;
            }

            return (completed, Array.IndexOf(workerTasks, completed));
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // Intentional stop (shutdown or rebalance) — do not trigger restart.
            return null;
        }
    }

    private async Task RunMonitorAsync(CancellationToken stopToken)
    {
        var result = await WaitForWorkerFaultAsync(workerTasks!, executionToken, stopToken)
            .ConfigureAwait(false);

        if (result is null)
        {
            return;
        }

        var (completed, workerIndex) = result.Value;
        var workerId = workerIndex >= 0 && workers is not null
            ? workers[workerIndex].Id
            : "Worker[?]";

        kafkaMetrics.RecordWorkerFault(registration.GroupId);

        if (completed.IsFaulted)
        {
            logger.LogError(completed.Exception,
                "{WorkerId} faulted in group '{GroupId}' on topic '{Topic}'. Triggering pool restart.",
                workerId, registration.GroupId, registration.TopicName);

            await observerInvoker.OnConsumerFaultedAsync(
                new ConsumerFaultedEvent(registration.GroupId, registration.TopicName, completed.Exception!))
                .ConfigureAwait(false);
        }
        else
        {
            logger.LogWarning(
                "{WorkerId} completed unexpectedly in group '{GroupId}' on topic '{Topic}'. Triggering pool restart.",
                workerId, registration.GroupId, registration.TopicName);
        }

        // Signal the poll loop to restart the pool.
        isFaulted = true;

        // Complete all worker channels to unblock any pending WriteAsync in the poll loop.
        if (workers is not null)
        {
            foreach (var worker in workers)
            {
                worker.Complete();
            }
        }
    }
}

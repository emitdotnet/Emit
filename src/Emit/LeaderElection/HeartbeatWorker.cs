namespace Emit.LeaderElection;

using Emit.Abstractions;
using Emit.Abstractions.LeaderElection;
using Emit.Configuration;
using Emit.Daemon;
using Emit.Observability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Background service that performs periodic heartbeats and leader election.
/// </summary>
internal sealed class HeartbeatWorker : BackgroundService, ILeaderElectionService
{
    private readonly ILeaderElectionPersistence persistence;
    private readonly DaemonCoordinator daemonCoordinator;
    private readonly LeaderElectionObserverInvoker observerInvoker;
    private readonly LeaderElectionOptions options;
    private readonly TimeProvider timeProvider;
    private readonly TaskScheduler callbackScheduler;
    private readonly ILogger<HeartbeatWorker> logger;
    private readonly string serviceName;
    private readonly string instanceId;

    // Heartbeats that may have written this node's lease: the last successful one and those that failed since
    private readonly List<Guid> leaseHeartbeatIds = [];

    private enum WorkerState { Unregistered, Follower, Leader }

    private LeaseTokenSource? leadershipCts;
    private LeaseTokenSource? workerCts;
    private WorkerState workerState = WorkerState.Unregistered;
    private ClockReading? lastHeartbeatStartedAt;
    private ObservedLease? observedLease;

    // When the last heartbeat that failed started. A failed heartbeat may have written the lease, and a node whose
    // link to the database keeps failing must not keep winning it back: the node claims it again only once its
    // heartbeats have succeeded for a lease duration since.
    private ClockReading? lastHeartbeatFailedAt;

    // Until its first heartbeat the node is presumed able to run daemons; afterwards it is only while it holds its
    // lease, which a node whose heartbeats or assignment reads fail never gets
    private bool heartbeatAttempted;

    // Whether the assignments an earlier process left under this node's id were given up
    private bool earlierProcessAssignmentsReleased;

    public HeartbeatWorker(
        ILeaderElectionPersistence persistence,
        DaemonCoordinator daemonCoordinator,
        LeaderElectionObserverInvoker observerInvoker,
        IOptions<LeaderElectionOptions> options,
        INodeIdentity nodeIdentity,
        TimeProvider timeProvider,
        ILogger<HeartbeatWorker> logger)
        : this(persistence, daemonCoordinator, observerInvoker, options, nodeIdentity, timeProvider, TaskScheduler.Default, logger)
    {
    }

    /// <summary>
    /// Creates a worker that runs the callbacks of the tokens it cancels on <paramref name="callbackScheduler"/>.
    /// </summary>
    internal HeartbeatWorker(
        ILeaderElectionPersistence persistence,
        DaemonCoordinator daemonCoordinator,
        LeaderElectionObserverInvoker observerInvoker,
        IOptions<LeaderElectionOptions> options,
        INodeIdentity nodeIdentity,
        TimeProvider timeProvider,
        TaskScheduler callbackScheduler,
        ILogger<HeartbeatWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        ArgumentNullException.ThrowIfNull(daemonCoordinator);
        ArgumentNullException.ThrowIfNull(observerInvoker);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(nodeIdentity);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(callbackScheduler);
        ArgumentNullException.ThrowIfNull(logger);

        this.persistence = persistence;
        this.daemonCoordinator = daemonCoordinator;
        this.observerInvoker = observerInvoker;
        this.options = options.Value;
        this.timeProvider = timeProvider;
        this.callbackScheduler = callbackScheduler;
        this.logger = logger;

        // A replaced identity is not covered by the options validation, and a service name the database
        // rejects would fail every heartbeat
        if (string.IsNullOrWhiteSpace(nodeIdentity.ServiceName) || nodeIdentity.ServiceName.Length > ValidationConstants.MaxServiceNameLength)
        {
            throw new ArgumentException(
                $"The node identity must have a service name of at most {ValidationConstants.MaxServiceNameLength} characters.",
                nameof(nodeIdentity));
        }

        // An empty id is how leader election and placement say "no node"
        if (nodeIdentity.NodeId == Guid.Empty)
        {
            throw new ArgumentException("The node identity must have a node id other than Guid.Empty.", nameof(nodeIdentity));
        }

        NodeId = nodeIdentity.NodeId;
        serviceName = nodeIdentity.ServiceName;
        instanceId = this.options.InstanceId ?? Environment.MachineName;
    }

    /// <inheritdoc />
    public Guid NodeId { get; }

    /// <inheritdoc />
    public bool IsLeader =>
        workerState == WorkerState.Leader
        && leadershipCts is { IsCancellationRequested: false };

    /// <inheritdoc />
    public CancellationToken LeadershipToken => leadershipCts?.Token ?? new CancellationToken(canceled: true);

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // The host stopped waiting before the node handed its daemons over. It carries on shutting down meanwhile,
        // but a process that exits now leaves them where they are.
        if (ExecuteTask is { IsCompleted: false })
        {
            logger.LogWarning(
                "The host stopped waiting before node {NodeId} finished shutting down; the daemons it did not hand over stay " +
                "assigned to it until its registration expires, up to {NodeRegistrationTtl}. Give the host, and any orchestrator, " +
                "a longer shutdown timeout.",
                NodeId, options.NodeRegistrationTtl);
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Leader election starting: serviceName={ServiceName}, nodeId={NodeId}, instanceId={InstanceId}, heartbeatInterval={HeartbeatInterval}, leaseDuration={LeaseDuration}",
            serviceName, NodeId, instanceId, options.HeartbeatInterval, options.RecordedLease);

        using var timer = new PeriodicTimer(options.HeartbeatInterval, timeProvider);

        try
        {
            // Perform an immediate heartbeat before entering the periodic loop
            await PerformHeartbeatAsync(stoppingToken).ConfigureAwait(false);

            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await PerformHeartbeatAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        finally
        {
            await ShutdownAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Performs a single heartbeat: registers the node, attempts leader election and runs daemon coordination.
    /// </summary>
    internal async Task PerformHeartbeatAsync(CancellationToken stoppingToken)
    {
        // The lease and the registration this heartbeat writes are written after this moment, and other nodes
        // wait their whole duration after they first see them, so a lease measured from here ends before anyone
        // can take it over. Durations are measured on both the monotonic and the wall clock, so neither a clock
        // step nor a suspend can make one last longer.
        var heartbeatStartedAt = ClockReading.Now(timeProvider);
        var request = new HeartbeatRequest(
            serviceName,
            NodeId,
            instanceId,
            daemonCoordinator.GetDeclaredDaemonIds(canRunDaemons: workerCts is null ? !heartbeatAttempted : !workerCts.IsCancellationRequested),
            options.RecordedLease,
            options.RecordedRegistrationTtl,
            Guid.NewGuid(),
            LeaseToTakeOver(),
            ClaimsLease: (lastHeartbeatFailedAt is not { } failedAt || failedAt.Elapsed(timeProvider) >= options.LeaseDuration)
                && daemonCoordinator.HasObservedServiceWithin(options.LeaseDuration));
        heartbeatAttempted = true;

        // Only a heartbeat that claims the lease can write it, and none does after a failure until one succeeds
        if (request.ClaimsLease)
        {
            leaseHeartbeatIds.Add(request.HeartbeatId);
        }

        try
        {
            // Not cancelled when the host stops: a heartbeat the node gave up on can still reach the database after
            // the node deregistered, and register it again. It makes several database calls, each given a query
            // timeout, and ends within the heartbeat timeout either way.
            var result = await QueryAsync(
                token => persistence.HeartbeatAsync(request, token), options.HeartbeatTimeout, CancellationToken.None).ConfigureAwait(false);
            ObserveLease(result);
            leaseHeartbeatIds.RemoveAll(id => id != request.HeartbeatId);
            if (result.LeaderNodeId == NodeId && result.LeaseHeartbeatId != request.HeartbeatId)
            {
                // An earlier heartbeat wrote the lease recorded for this node, possibly one that failed or reached the
                // database late; a heartbeat that does not claim the lease reports it too
                leaseHeartbeatIds.Add(result.LeaseHeartbeatId);
            }

            // Fire node registered observer on first successful heartbeat
            if (workerState == WorkerState.Unregistered)
            {
                workerState = WorkerState.Follower;
                logger.LogInformation("Node {NodeId} registered in cluster", NodeId);
                observerInvoker.OnNodeRegistered(NodeId);
            }

            // The registration was last refreshed no earlier than the previous heartbeat started. Once that is a
            // registration TTL ago, the leader may have removed it and be moving this node's daemons elsewhere.
            if (lastHeartbeatStartedAt is { } previous && previous.Elapsed(timeProvider) >= options.ReleaseAfter)
            {
                logger.LogWarning(
                    "Node {NodeId} did not heartbeat for {NodeRegistrationTtl} or longer; it gives up its daemons",
                    NodeId, options.NodeRegistrationTtl);
                await daemonCoordinator.ReleaseAssignmentsAsync(NodeId, stoppingToken).ConfigureAwait(false);
            }

            lastHeartbeatStartedAt = heartbeatStartedAt;

            await HandleHeartbeatResultAsync(result, heartbeatStartedAt, stoppingToken).ConfigureAwait(false);

            logger.LogDebug(
                "Heartbeat succeeded for node {NodeId}, isLeader={IsLeader}, leaderNodeId={LeaderNodeId}",
                NodeId, IsLeader, result.LeaderNodeId);

            if (await ReleaseEarlierProcessAssignmentsAsync(stoppingToken).ConfigureAwait(false))
            {
                await CoordinateAsync(heartbeatStartedAt, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The application is shutting down; the finally block in ExecuteAsync handles cleanup
            throw;
        }
        catch (Exception ex)
        {
            // The daemons keep running until the lease of the last successful heartbeat runs out
            logger.LogError(ex, "Heartbeat failed for node {NodeId}", NodeId);
            lastHeartbeatFailedAt = heartbeatStartedAt;

            var droppedLeadership = CancellationToken.None;
            if (workerState == WorkerState.Leader)
            {
                logger.LogWarning("Node {NodeId} lost leadership due to heartbeat failure", NodeId);
                droppedLeadership = DropLeadership();
                observerInvoker.OnLeaderLost(NodeId);
            }

            // The lease may have been renewed before a later step failed. A node that does not lead must not
            // hold it, or no other node can take over while this one keeps failing.
            await ResignAsync(droppedLeadership, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gives up, once, the assignments recorded under this node's id before it first coordinates, and returns
    /// whether it may coordinate. A process was given nothing before it registered: assignments under its id belong
    /// to an earlier process with the same id, whose daemons the leader may be moving elsewhere. Until they are given
    /// up the node holds no lease, so it runs none of them.
    /// </summary>
    private async Task<bool> ReleaseEarlierProcessAssignmentsAsync(CancellationToken stoppingToken)
    {
        if (earlierProcessAssignmentsReleased)
        {
            return true;
        }

        try
        {
            await daemonCoordinator.ReleaseAssignmentsAsync(NodeId, stoppingToken).ConfigureAwait(false);
            earlierProcessAssignmentsReleased = true;
            return true;
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Node {NodeId} could not give up assignments left under its id; it runs no daemon until it does", NodeId);
            return false;
        }
    }

    /// <summary>
    /// Coordinates daemons until the next heartbeat is due. Every query is bounded, but their number is not, and
    /// a tick that ran past the lease would cost this node its lease and its daemons. Work left when the next
    /// heartbeat is due resumes on it.
    /// </summary>
    private async Task CoordinateAsync(ClockReading heartbeatStartedAt, CancellationToken stoppingToken)
    {
        var budget = options.HeartbeatInterval - heartbeatStartedAt.Elapsed(timeProvider);
        if (budget <= TimeSpan.Zero)
        {
            return;
        }

        using var nextHeartbeatDue = new CancellationTokenSource(budget, timeProvider);
        using var tick = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, nextHeartbeatDue.Token);
        try
        {
            await daemonCoordinator.OnHeartbeatTickAsync(
                NodeId, LeadershipToken, () => HoldLease(heartbeatStartedAt), tick.Token).ConfigureAwait(false);
        }
        catch (Exception) when (nextHeartbeatDue.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            // Whatever an interrupted database call throws, the time ran out
            logger.LogWarning(
                "Daemon coordination of node {NodeId} did not finish within the heartbeat interval; it resumes on the next heartbeat",
                NodeId);
        }
    }

    /// <summary>
    /// Renews this node's lease to <see cref="LeaderElectionOptions.LeaseDuration"/> after the heartbeat that
    /// started at <paramref name="heartbeatStartedAt"/>, and returns it. The daemons started under it stop when it
    /// would have expired without another renewal, before the node's registration expires and the leader moves them
    /// elsewhere. A lease that ran out is replaced, never revived, so a run started under it stays fenced.
    /// </summary>
    private LeaseTokenSource HoldLease(ClockReading heartbeatStartedAt)
    {
        if (workerCts is null || workerCts.IsCancellationRequested)
        {
            workerCts?.Dispose();
            workerCts = NewLease();
        }

        workerCts.ExpireAt(heartbeatStartedAt, options.HeldLease);
        return workerCts;
    }

    /// <summary>
    /// Gives up the lease this node's heartbeats may have written, so another node can take it over at once.
    /// Code holding the token of a leadership this node just dropped must see it cancelled first; if it does
    /// not before the lease would have run out anyway, the lease is left to expire.
    /// </summary>
    private async Task ResignAsync(CancellationToken droppedLeadership, CancellationToken cancellationToken)
    {
        if (!await HasEndedAsync(droppedLeadership, cancellationToken).ConfigureAwait(false))
        {
            logger.LogWarning("The leadership token of node {NodeId} was not cancelled in time; its lease expires on its own", NodeId);
            return;
        }

        try
        {
            await QueryAsync(token => persistence.ResignLeadershipAsync(serviceName, NodeId, [.. leaseHeartbeatIds], token), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to give up the lease of node {NodeId}; it expires on its own", NodeId);
        }
    }

    /// <summary>
    /// Returns whether the token of a dropped leadership reads cancelled before the lease would have run out.
    /// Its cancellation runs on the callback scheduler, which can be slow to get to it.
    /// </summary>
    private async Task<bool> HasEndedAsync(CancellationToken leadership, CancellationToken cancellationToken)
    {
        if (!leadership.CanBeCanceled || leadership.IsCancellationRequested)
        {
            return true;
        }

        var remaining = lastHeartbeatStartedAt is { } renewedAt ? options.HeldLease - renewedAt.Elapsed(timeProvider) : TimeSpan.Zero;
        if (remaining <= TimeSpan.Zero)
        {
            return false;
        }

        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = leadership.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), ended);
        try
        {
            await ended.Task.WaitAsync(remaining, timeProvider, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Runs one database operation, bounded by <see cref="LeaderElectionOptions.QueryTimeout"/>.
    /// </summary>
    private Task<T> QueryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        QueryAsync(operation, options.QueryTimeout, cancellationToken);

    /// <summary>
    /// Runs a database operation, bounded by <paramref name="timeout"/>.
    /// </summary>
    private async Task<T> QueryAsync<T>(Func<CancellationToken, Task<T>> operation, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeoutSource = new CancellationTokenSource(timeout, timeProvider);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        // Also when the client does not honour cancellation: what the abandoned call does late is a late write
        return await operation(bounded.Token).WaitAsync(bounded.Token).ConfigureAwait(false);
    }

    private Task QueryAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        QueryAsync(async token =>
        {
            await operation(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    private async Task HandleHeartbeatResultAsync(HeartbeatResult result, ClockReading heartbeatStartedAt, CancellationToken stoppingToken)
    {
        var holdsValidLease = workerState == WorkerState.Leader && leadershipCts is { IsCancellationRequested: false };

        if (result.IsLeader && !holdsValidLease)
        {
            if (workerState == WorkerState.Leader)
            {
                // The previous lease ran out before it was renewed, so that leadership term ended
                DropLeadership();
                logger.LogWarning("Node {NodeId} lease expired before it was renewed", NodeId);
                observerInvoker.OnLeaderLost(NodeId);
            }

            // Became leader; the token is cancelled when the lease runs out without renewal
            workerState = WorkerState.Leader;
            leadershipCts?.Dispose();
            leadershipCts = NewLease();
            leadershipCts.ExpireAt(heartbeatStartedAt, options.HeldLease);

            logger.LogInformation("Node {NodeId} elected as leader of service {ServiceName}", NodeId, serviceName);
            observerInvoker.OnLeaderElected(NodeId);
        }
        else if (result.IsLeader)
        {
            leadershipCts!.ExpireAt(heartbeatStartedAt, options.HeldLease);
            logger.LogDebug("Node {NodeId} renewed leadership lease", NodeId);
        }
        else if (!result.IsLeader && workerState == WorkerState.Leader)
        {
            var droppedLeadership = DropLeadership();
            observerInvoker.OnLeaderLost(NodeId);
            if (result.LeaderNodeId == NodeId)
            {
                // It stopped claiming the lease, which it still holds: another node takes it over at once
                logger.LogWarning(
                    "Node {NodeId} could not observe service {ServiceName} for {LeaseDuration}; it gives up its leadership",
                    NodeId, serviceName, options.LeaseDuration);
                await ResignAsync(droppedLeadership, stoppingToken).ConfigureAwait(false);
            }
            else
            {
                logger.LogWarning("Node {NodeId} lost leadership to {LeaderNodeId}", NodeId, result.LeaderNodeId);
            }
        }
    }

    /// <summary>
    /// Creates a lease source. A suspended process notices its lease ran out within a query timeout of resuming,
    /// the time the node already allows any one database call.
    /// </summary>
    private LeaseTokenSource NewLease() => new(timeProvider, callbackScheduler, options.QueryTimeout, logger);

    /// <summary>
    /// Records the service's lease as this heartbeat saw it. Another node's lease is watched from the moment
    /// its current heartbeat is first seen, which is no earlier than its holder wrote it.
    /// </summary>
    private void ObserveLease(HeartbeatResult result)
    {
        if (result.IsLeader || result.LeaseHeartbeatId == Guid.Empty)
        {
            observedLease = null;
        }
        else if (observedLease?.HeartbeatId != result.LeaseHeartbeatId)
        {
            observedLease = new ObservedLease(result.LeaseHeartbeatId, result.LeaseDuration, timeProvider.GetTimestamp());
        }
    }

    /// <summary>
    /// Returns the heartbeat of another node's lease this node saw go unrenewed for its whole duration, on its own
    /// monotonic clock, which never counts more time than has passed: the lease can be taken over if it is still the
    /// one recorded.
    /// </summary>
    private Guid? LeaseToTakeOver() =>
        observedLease is { } lease && timeProvider.GetElapsedTime(lease.Since) >= lease.Duration ? lease.HeartbeatId : null;

    /// <summary>
    /// Gives up leadership, stops daemons and deregisters the node, then lets the observers hear of it. Leadership
    /// does not depend on the daemons, so it is handed over before waiting for them to stop.
    /// </summary>
    internal async Task ShutdownAsync()
    {
        logger.LogInformation("Leader election shutting down for node {NodeId}", NodeId);

        try
        {
            var droppedLeadership = DropLeadership();

            // Also after a failed heartbeat, which may have renewed the lease while this node stepped down
            await ResignAsync(droppedLeadership, CancellationToken.None).ConfigureAwait(false);

            // The leader gives a leaving node no more daemons, and starts asking it to stop the ones it runs
            await WithdrawDaemonsAsync().ConfigureAwait(false);

            await daemonCoordinator.StopAllDaemonsAsync(NodeId).ConfigureAwait(false);

            // The daemons that stopped move at once; one still stopping keeps its assignment
            await ReleaseAssignmentsOnShutdownAsync().ConfigureAwait(false);

            if (daemonCoordinator.HasRunsStillStopping)
            {
                // Deregistering would let the leader start those daemons elsewhere at once
                logger.LogWarning(
                    "Node {NodeId} has daemons still stopping; it stays registered until its registration expires",
                    NodeId);
            }
            else
            {
                await QueryAsync(token => persistence.DeregisterNodeAsync(NodeId, token), CancellationToken.None).ConfigureAwait(false);
                logger.LogInformation("Node {NodeId} deregistered", NodeId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error during leader election shutdown for node {NodeId}", NodeId);
        }
        finally
        {
            DropLeadership();
            workerCts?.Dispose();
            workerCts = null;

            // The host disposes the observers once this returns
            await Task.WhenAll(
                observerInvoker.FlushAsync(options.QueryTimeout, timeProvider),
                daemonCoordinator.FlushObserversAsync(options.QueryTimeout)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Registers this node one last time, declaring no daemon, as a node leaving its service.
    /// </summary>
    private async Task WithdrawDaemonsAsync()
    {
        var request = new HeartbeatRequest(
            serviceName,
            NodeId,
            instanceId,
            [],
            options.RecordedLease,
            options.RecordedRegistrationTtl,
            Guid.NewGuid(),
            ClaimsLease: false);
        try
        {
            await QueryAsync(token => persistence.HeartbeatAsync(request, token), options.HeartbeatTimeout, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Node {NodeId} could not withdraw its daemons before shutting down", NodeId);
        }
    }

    /// <summary>
    /// Gives up this node's assignments once its daemons stopped, so the leader places them elsewhere at once, even
    /// if a heartbeat that reaches the database late registers the node again for a while.
    /// </summary>
    private async Task ReleaseAssignmentsOnShutdownAsync()
    {
        try
        {
            await daemonCoordinator.ReleaseAssignmentsAsync(NodeId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Node {NodeId} could not give up its daemons; they move once its registration is gone", NodeId);
        }
    }

    /// <summary>
    /// Ends this node's leadership and returns the token of the leadership dropped, or one that cannot be
    /// cancelled when the node did not lead.
    /// </summary>
    private CancellationToken DropLeadership()
    {
        workerState = WorkerState.Follower;
        if (leadershipCts is null)
        {
            return CancellationToken.None;
        }

        var dropped = leadershipCts.Token;
        leadershipCts.Cancel();
        leadershipCts.Dispose();
        leadershipCts = null;
        return dropped;
    }

    /// <summary>
    /// Another node's lease, and since when this node has seen it without a new heartbeat.
    /// </summary>
    private sealed record ObservedLease(Guid HeartbeatId, TimeSpan Duration, long Since);
}

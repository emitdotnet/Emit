namespace Emit.Daemon;

using Emit.Abstractions;
using Emit.Abstractions.Daemon;
using Emit.Abstractions.LeaderElection;
using Emit.Configuration;
using Emit.LeaderElection;
using Emit.Metrics;
using Emit.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Coordinates daemons on every heartbeat. The leader of the service places the daemons declared by the
/// service's live nodes; every node runs the daemons assigned to it. Called by HeartbeatWorker.
/// </summary>
/// <remarks>
/// <para>
/// Leader writes are compare-and-swap on the assignment the leader read, so a decision based on a stale
/// read is never applied, and they stop as soon as the leadership lease ends. Every node of the service observes
/// its assignments and nodes on each heartbeat, on its own monotonic clock, so neither clock skew nor a change of
/// leader can make a timeout fire early or start over. A daemon is moved off a node directly only when the node
/// is gone, or never acknowledged it and so never started it. A node that is alive but no longer declares a
/// daemon it runs is asked to stop it, and the daemon moves once the node confirmed the drain or certainly
/// stopped it.
/// </para>
/// <para>
/// The heartbeat never waits on daemon code: runs start and are cancelled on the thread pool, each on its own,
/// and every database call is bounded by <see cref="LeaderElectionOptions.QueryTimeout"/>. A cancelled run
/// still counts as running until it ends: the node does not start the daemon again or confirm its drain
/// before then.
/// </para>
/// <para>
/// A run that ends before it was asked to stop is a failure: the daemon is started again, and after
/// <see cref="DaemonOptions.MaxConsecutiveFailures"/> failures without a run lasting
/// <see cref="DaemonOptions.FailureCooldown"/>, the node stops declaring it for that long, so the leader moves it.
/// </para>
/// </remarks>
internal sealed class DaemonCoordinator
{
    /// <summary>
    /// A node that missed more than one heartbeat is not given new work.
    /// </summary>
    private TimeSpan ResponsiveWindow => (leaderElectionOptions.HeartbeatInterval * 2) + leaderElectionOptions.QueryTimeout;

    private readonly IDaemonAssignmentPersistence assignmentPersistence;
    private readonly ILeaderElectionPersistence leaderElectionPersistence;
    // Each daemon under the identifier it had when the coordinator was created, so an agent whose identifier
    // changes later is still run under one identifier, and never twice
    private readonly Dictionary<string, IDaemonAgent> daemons;
    private readonly LeaderElectionObserverInvoker leaderElectionObserverInvoker;
    private readonly NodeObservations nodeObservations;
    private readonly DaemonObserverInvoker observerInvoker;
    private readonly DaemonOptions options;
    private readonly LeaderElectionOptions leaderElectionOptions;
    private readonly TimeProvider timeProvider;
    private readonly TaskScheduler runScheduler;
    private readonly DaemonMetrics metrics;
    private readonly ILogger<DaemonCoordinator> logger;
    private readonly string serviceName;
    private readonly Guid ownNodeId;

    // The latest run of each daemon, read by the metrics on the exporter's thread; the set of daemons never changes
    private readonly Dictionary<string, RunSlot> runSlots;

    private readonly Dictionary<string, RunningDaemon> runningDaemons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StoppingRun> stoppingRuns = new(StringComparer.Ordinal);

    // Generations kept through a release only because their run had not ended: released once it does
    private readonly Dictionary<string, long> releaseOnceStopped = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> consecutiveFailures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> cooldownStartedAt = new(StringComparer.Ordinal);
    private Dictionary<PendingAssignment, long> pendingSince = [];

    // When each node of the service was last seen no longer offering a daemon it ran, as a node does with one it
    // keeps failing
    private Dictionary<Guid, long> gaveUpDaemonAt = [];

    // Daemons declared in the node's last heartbeat; every daemon until the first one
    private HashSet<string> declaredDaemonIds;

    // When the node last read its service's assignments and nodes; it is presumed able to until it tries
    private long serviceObservedAt;

    // The daemons other live nodes of the service offered when this node last read their registrations
    private volatile IReadOnlySet<string> offeredByOthers = new HashSet<string>(StringComparer.Ordinal);

    public DaemonCoordinator(
        IDaemonAssignmentPersistence assignmentPersistence,
        ILeaderElectionPersistence leaderElectionPersistence,
        IEnumerable<IDaemonAgent> daemons,
        DaemonObserverInvoker observerInvoker,
        LeaderElectionObserverInvoker leaderElectionObserverInvoker,
        IOptions<DaemonOptions> options,
        IOptions<LeaderElectionOptions> leaderElectionOptions,
        INodeIdentity nodeIdentity,
        TimeProvider timeProvider,
        DaemonMetrics metrics,
        ILogger<DaemonCoordinator> logger)
        : this(
            assignmentPersistence,
            leaderElectionPersistence,
            daemons,
            observerInvoker,
            leaderElectionObserverInvoker,
            options,
            leaderElectionOptions,
            nodeIdentity,
            timeProvider,
            new NodeObservations(timeProvider),
            TaskScheduler.Default,
            metrics,
            logger)
    {
    }

    /// <summary>
    /// Creates a coordinator that judges nodes with <paramref name="nodeObservations"/>, and starts and cancels
    /// daemon runs on <paramref name="runScheduler"/>.
    /// </summary>
    internal DaemonCoordinator(
        IDaemonAssignmentPersistence assignmentPersistence,
        ILeaderElectionPersistence leaderElectionPersistence,
        IEnumerable<IDaemonAgent> daemons,
        DaemonObserverInvoker observerInvoker,
        LeaderElectionObserverInvoker leaderElectionObserverInvoker,
        IOptions<DaemonOptions> options,
        IOptions<LeaderElectionOptions> leaderElectionOptions,
        INodeIdentity nodeIdentity,
        TimeProvider timeProvider,
        NodeObservations nodeObservations,
        TaskScheduler runScheduler,
        DaemonMetrics metrics,
        ILogger<DaemonCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(assignmentPersistence);
        ArgumentNullException.ThrowIfNull(leaderElectionPersistence);
        ArgumentNullException.ThrowIfNull(daemons);
        ArgumentNullException.ThrowIfNull(observerInvoker);
        ArgumentNullException.ThrowIfNull(leaderElectionObserverInvoker);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(leaderElectionOptions);
        ArgumentNullException.ThrowIfNull(nodeIdentity);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(nodeObservations);
        ArgumentNullException.ThrowIfNull(runScheduler);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);

        this.assignmentPersistence = assignmentPersistence;
        this.leaderElectionPersistence = leaderElectionPersistence;
        this.daemons = IndexDaemons(daemons);
        this.observerInvoker = observerInvoker;
        this.leaderElectionObserverInvoker = leaderElectionObserverInvoker;
        this.options = options.Value;
        this.leaderElectionOptions = leaderElectionOptions.Value;
        this.timeProvider = timeProvider;
        this.runScheduler = runScheduler;
        this.metrics = metrics;
        this.logger = logger;
        this.nodeObservations = nodeObservations;
        serviceName = nodeIdentity.ServiceName;
        ownNodeId = nodeIdentity.NodeId;

        declaredDaemonIds = this.daemons.Keys.ToHashSet(StringComparer.Ordinal);
        serviceObservedAt = timeProvider.GetTimestamp();
        runSlots = this.daemons.Keys.ToDictionary(daemonId => daemonId, _ => new RunSlot(), StringComparer.Ordinal);
        metrics.ObserveRunsWith(CountRuns);
    }

    /// <summary>
    /// Gets a value indicating whether a cancelled daemon run on this node has not ended yet.
    /// </summary>
    public bool HasRunsStillStopping => stoppingRuns.Values.Any(run => !run.Run.IsCompleted);

    /// <summary>
    /// Returns the identifiers of the daemons this node offers to run. Called for each heartbeat, whose
    /// registration carries them; until the next call, the node starts only daemons it declared, since the leader
    /// decides from that registration. A node that cannot run daemons, because it failed to read its assignments
    /// or its lease ran out, offers none, so the leader neither gives it daemons it could not start nor leaves
    /// daemons on it when its heartbeats reach the database without it knowing. Otherwise it offers every
    /// registered daemon except those cooling down after repeated failures, and those whose cancelled run
    /// outlived the drain timeout, which it cannot run again before that run ends.
    /// </summary>
    public IReadOnlyList<string> GetDeclaredDaemonIds(bool canRunDaemons)
    {
        string[] declared = canRunDaemons
            ? [.. daemons.Keys.Where(daemonId => !IsCoolingDown(daemonId) && !IsStuckStopping(daemonId))]
            : [];
        declaredDaemonIds = declared.ToHashSet(StringComparer.Ordinal);
        return declared;
    }

    /// <summary>
    /// Returns whether this node read its service's assignments and nodes within <paramref name="duration"/>, or
    /// was created within it. A node that cannot should not lead: it could not place the service's daemons.
    /// </summary>
    public bool HasObservedServiceWithin(TimeSpan duration) =>
        timeProvider.GetElapsedTime(serviceObservedAt) < duration;

    /// <summary>
    /// Called by HeartbeatWorker after each successful heartbeat. The node runs its own assignments, holding its
    /// lease through <paramref name="holdLease"/> once it has read them; meanwhile it observes its service, and
    /// places the service's daemons while <paramref name="leadershipToken"/> is not cancelled. Both write
    /// compare-and-swap on what they read, so neither waits for the other, and one that is slow does not take the
    /// other's share of the heartbeat.
    /// </summary>
    /// <param name="nodeId">The identifier of this node.</param>
    /// <param name="leadershipToken">Cancelled when this node does not lead its service.</param>
    /// <param name="holdLease">Renews the node's lease and returns it; the daemons started now run under it.</param>
    /// <param name="stoppingToken">Cancelled when the heartbeat runs out of time or the node shuts down.</param>
    public Task OnHeartbeatTickAsync(
        Guid nodeId,
        CancellationToken leadershipToken,
        Func<LeaseTokenSource> holdLease,
        CancellationToken stoppingToken) =>
        Task.WhenAll(
            ProcessNodeAssignmentsAsync(nodeId, holdLease, stoppingToken),
            CoordinateServiceAsync(leadershipToken, stoppingToken));

    /// <summary>
    /// Cancels all running daemons and waits up to <see cref="DaemonOptions.DrainTimeout"/> for every
    /// cancelled run to end. Observers hear of each run that ended. Called during application shutdown.
    /// </summary>
    public async Task StopAllDaemonsAsync(Guid nodeId)
    {
        foreach (var daemonId in runningDaemons.Keys.ToList())
        {
            StopDaemon(daemonId, nodeId);
        }

        var stillRunning = stoppingRuns.Values.Select(r => r.Run).Where(run => !run.IsCompleted).ToList();
        if (stillRunning is not [])
        {
            try
            {
                await Task.WhenAll(stillRunning).WaitAsync(options.DrainTimeout, timeProvider).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                logger.LogWarning(
                    "{Count} daemon(s) did not stop within {DrainTimeout} after they were cancelled",
                    stillRunning.Count(run => !run.IsCompleted), options.DrainTimeout);
            }
            catch (Exception)
            {
                // A run that faulted while stopping has stopped
            }
        }

        // Completes the stop of every run that ended, which notifies the observers; one still going is not reported
        foreach (var daemonId in stoppingRuns.Keys.ToList())
        {
            _ = HasStoppingRun(daemonId);
        }
    }

    /// <summary>
    /// Waits, at most <paramref name="bound"/>, for the observers to be told of everything that happened so far.
    /// Called last during application shutdown, before the host disposes them.
    /// </summary>
    public Task FlushObserversAsync(TimeSpan bound) =>
        Task.WhenAll(
            observerInvoker.FlushAsync(bound, timeProvider),
            leaderElectionObserverInvoker.FlushAsync(bound, timeProvider));

    /// <summary>
    /// Stops this node's daemons and gives up its assignments. Called when the node's registration may have
    /// expired: the leader then moves the node's daemons, so resuming them could run a daemon on two nodes.
    /// A daemon whose cancelled run has not ended keeps its assignment.
    /// </summary>
    public async Task ReleaseAssignmentsAsync(Guid nodeId, CancellationToken cancellationToken)
    {
        foreach (var daemonId in runningDaemons.Keys.ToList())
        {
            StopDaemon(daemonId, nodeId);
        }

        var assignments = await QueryAsync(
            token => assignmentPersistence.GetNodeAssignmentsAsync(serviceName, nodeId, token), cancellationToken).ConfigureAwait(false);

        List<DaemonAssignment> releasing = [];
        foreach (var assignment in assignments)
        {
            if (HasStoppingRun(assignment.DaemonId))
            {
                // Kept until the run ends, then released rather than restarted: the leader may have decided
                // to move the daemon while this node looked gone, and that decision must not apply to a new run
                releaseOnceStopped[assignment.DaemonId] = assignment.Generation;
            }
            else
            {
                releasing.Add(assignment);
            }
        }

        // All at once, each failing harmlessly when the leader already moved the daemon
        await Task.WhenAll(releasing.Select(async assignment =>
        {
            var released = await QueryAsync(
                token => assignmentPersistence.RemoveAsync(serviceName, assignment.DaemonId, assignment.Generation, token),
                cancellationToken).ConfigureAwait(false);
            if (released)
            {
                logger.LogInformation("Node {NodeId} released daemon {DaemonId}", nodeId, assignment.DaemonId);
            }
        })).ConfigureAwait(false);
    }

    // --- Service: observation and placement ---

    /// <summary>
    /// Reads and observes the service's assignments and nodes; the leader then places the service's daemons.
    /// </summary>
    private async Task CoordinateServiceAsync(CancellationToken leadershipToken, CancellationToken stoppingToken)
    {
        var leads = !leadershipToken.IsCancellationRequested;
        using var leading = CancellationTokenSource.CreateLinkedTokenSource(leadershipToken, stoppingToken);
        var token = leads ? leading.Token : stoppingToken;

        try
        {
            // Assignments are read before nodes, so a node missing from the second read has left or expired
            // rather than registered after the first
            var readStartedAt = timeProvider.GetTimestamp();
            var assignments = await QueryAsync(
                t => assignmentPersistence.GetAllAssignmentsAsync(serviceName, t), token).ConfigureAwait(false);
            var registrations = await QueryAsync(
                t => leaderElectionPersistence.GetNodesAsync(serviceName, t), token).ConfigureAwait(false);
            var now = timeProvider.GetTimestamp();
            var observed = nodeObservations.Observe(registrations, readStartedAt);
            TrackPendingAssignments(assignments, now);
            TrackDaemonsGivenUp(assignments, observed, now);
            offeredByOthers = observed
                .Where(n => n.IsAlive && n.Registration.NodeId != ownNodeId)
                .SelectMany(n => n.Registration.DaemonIds)
                .ToHashSet(StringComparer.Ordinal);
            serviceObservedAt = now;

            if (leads)
            {
                await ReconcileAsync(assignments, observed, now, token).ConfigureAwait(false);
            }
        }
        catch (Exception) when (leads && leadershipToken.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            // Whatever an interrupted database call throws, the leadership ended
            logger.LogInformation("Leadership of service {ServiceName} ended during reconciliation; stopped placing daemons", serviceName);
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to observe or reconcile the daemons of service {ServiceName}", serviceName);
        }
    }

    private async Task ReconcileAsync(
        IReadOnlyList<DaemonAssignment> assignments,
        IReadOnlyList<ObservedNode> observed,
        long now,
        CancellationToken token)
    {
        // A node judged dead is let go only once its registration is removed, which fails when it renewed since it
        // was read: until then it keeps its daemons, but is given no new ones
        var removed = await RemoveDeadNodesAsync(
            [.. observed.Where(n => !n.IsAlive).Select(n => n.Registration)], token).ConfigureAwait(false);
        var notRemoved = observed
            .Where(n => !n.IsAlive && !removed.Contains(n.Registration.NodeId))
            .Select(n => n.Registration.NodeId)
            .ToHashSet();
        // A node is given new work once it was seen renewing recently
        List<LiveNode> liveNodes =
        [
            .. observed
                .Where(n => n.IsAlive)
                .Select(n => new LiveNode(
                    n.Registration.NodeId,
                    n.Registration.DaemonIds,
                    n.HasRenewed && n.Unrenewed < ResponsiveWindow,
                    gaveUpDaemonAt.TryGetValue(n.Registration.NodeId, out var gaveUpAt)
                        && timeProvider.GetElapsedTime(gaveUpAt, now) < options.FailureCooldown)),
        ];
        var registrationTtls = observed
            .Where(n => n.IsAlive)
            .ToDictionary(n => n.Registration.NodeId, n => n.Registration.RegistrationTtl);

        // The daemons to run are the ones declared by the service's live nodes, not only the leader's own
        var candidatesByDaemon = liveNodes
            .SelectMany(node => node.DaemonIds.Select(daemonId => (DaemonId: daemonId, Node: node)))
            .GroupBy(d => d.DaemonId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(d => d.Node).DistinctBy(n => n.NodeId).ToList(),
                StringComparer.Ordinal);
        var assignmentMap = assignments.ToDictionary(a => a.DaemonId, StringComparer.Ordinal);
        var load = assignments.GroupBy(a => a.AssignedNodeId).ToDictionary(g => g.Key, g => g.Count());
        var changed = false;

        foreach (var daemonId in assignmentMap.Keys.Union(candidatesByDaemon.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            assignmentMap.TryGetValue(daemonId, out var assignment);
            var candidates = candidatesByDaemon.GetValueOrDefault(daemonId) ?? [];
            var action = notRemoved.Contains(assignment?.AssignedNodeId ?? Guid.Empty)
                ? PlacementAction.Keep
                : Plan(daemonId, assignment, candidates, registrationTtls, now);
            if (action == PlacementAction.Keep)
            {
                continue;
            }

            changed = true;
            try
            {
                switch (action)
                {
                    case PlacementAction.Assign:
                        await AssignAsync(daemonId, candidates, load, assignment, token).ConfigureAwait(false);
                        break;

                    case PlacementAction.Revoke:
                        await RevokeAsync(assignment!, token).ConfigureAwait(false);
                        break;

                    case PlacementAction.Remove:
                        await RemoveAsync(assignment!, load, token).ConfigureAwait(false);
                        break;
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                // One daemon's failure does not hold up the placement of the others
                logger.LogWarning(ex, "Failed to place daemon {DaemonId}", daemonId);
            }
        }

        if (!changed)
        {
            await RebalanceAsync(assignments, candidatesByDaemon, load, token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Decides what the leader does with a daemon, given the live nodes that declare it and the registration TTL
    /// of every live node.
    /// </summary>
    /// <remarks>
    /// A revocation the node does not confirm is given up after the node's own registration TTL, which its options
    /// keep longer than its lease, drain timeout and one heartbeat together: a node that read its assignments after
    /// the revocation stopped the daemon, one that did not lost its lease, and a cancelled run ends within the drain
    /// timeout, as <see cref="IDaemonAgent"/> requires; a run that does not end is reported and may overlap.
    /// </remarks>
    private PlacementAction Plan(
        string daemonId,
        DaemonAssignment? assignment,
        List<LiveNode> candidates,
        Dictionary<Guid, TimeSpan> registrationTtls,
        long now)
    {
        var moveOut = candidates is [] ? PlacementAction.Remove : PlacementAction.Assign;
        if (assignment is null)
        {
            return candidates is [] ? PlacementAction.Keep : PlacementAction.Assign;
        }

        if (!registrationTtls.TryGetValue(assignment.AssignedNodeId, out var registrationTtl))
        {
            // The node left, or its registration expired after it fenced itself
            logger.LogInformation("Daemon {DaemonId} is assigned to node {NodeId}, which is gone", daemonId, assignment.AssignedNodeId);
            return moveOut;
        }

        var declared = candidates.Any(n => n.NodeId == assignment.AssignedNodeId);
        var pending = assignment.State == DaemonAssignmentState.Active
            ? TimeSpan.Zero
            : timeProvider.GetElapsedTime(pendingSince[PendingAssignment.Of(assignment)], now);
        switch (assignment.State)
        {
            case DaemonAssignmentState.Assigning when !declared || pending > options.AcknowledgeTimeout:
                // A reassignment is written only if the node still has not acknowledged it, so it never started the
                // daemon. Removing it would not check that, so with no other node to take it the node is asked to
                // stop it instead, which is safe whether or not it acknowledged meanwhile.
                logger.LogWarning(
                    "Daemon {DaemonId} was not acknowledged by node {NodeId} within {AcknowledgeTimeout}, or is no longer declared by it",
                    daemonId, assignment.AssignedNodeId, options.AcknowledgeTimeout);
                return candidates is [] ? PlacementAction.Revoke : PlacementAction.Assign;

            case DaemonAssignmentState.Active when !declared:
                logger.LogInformation(
                    "Daemon {DaemonId} runs on node {NodeId}, which no longer declares it; it is asked to stop it",
                    daemonId, assignment.AssignedNodeId);
                return PlacementAction.Revoke;

            case DaemonAssignmentState.Revoking when pending >= registrationTtl:
                logger.LogWarning(
                    "Node {NodeId} did not confirm it stopped daemon {DaemonId} within its registration TTL of {RegistrationTtl}; the daemon is moved",
                    assignment.AssignedNodeId, daemonId, registrationTtl);
                return moveOut;

            default:
                return PlacementAction.Keep;
        }
    }

    /// <summary>
    /// Records the nodes seen running a daemon they no longer offer while still offering others, and forgets those
    /// that left the service. A node that offers none, such as one whose lease ran out, gave up nothing in particular.
    /// </summary>
    private void TrackDaemonsGivenUp(IReadOnlyList<DaemonAssignment> assignments, IReadOnlyList<ObservedNode> observed, long now)
    {
        var declaring = observed.ToDictionary(n => n.Registration.NodeId, n => n.Registration.DaemonIds);
        Dictionary<Guid, long> next = [];
        foreach (var (node, gaveUpAt) in gaveUpDaemonAt.Where(g => declaring.ContainsKey(g.Key)))
        {
            next[node] = gaveUpAt;
        }

        foreach (var assignment in assignments.Where(a => a.State == DaemonAssignmentState.Active))
        {
            if (declaring.TryGetValue(assignment.AssignedNodeId, out var daemonIds) && daemonIds is not [] && !daemonIds.Contains(assignment.DaemonId))
            {
                next[assignment.AssignedNodeId] = now;
            }
        }

        gaveUpDaemonAt = next;
    }

    /// <summary>
    /// Records when the leader first saw each assignment waiting for a node.
    /// </summary>
    private void TrackPendingAssignments(IReadOnlyList<DaemonAssignment> assignments, long now)
    {
        var next = new Dictionary<PendingAssignment, long>();
        foreach (var assignment in assignments.Where(a => a.State != DaemonAssignmentState.Active))
        {
            var key = PendingAssignment.Of(assignment);
            next[key] = pendingSince.TryGetValue(key, out var firstSeen) ? firstSeen : now;
        }

        pendingSince = next;
    }

    /// <summary>
    /// Once every assignment has settled, revokes one daemon from the busiest node when a responsive node
    /// running at least two fewer daemons can take it, and has not given up a daemon within the failure cooldown.
    /// The drained daemon is then placed on the least busy node.
    /// </summary>
    private async Task RebalanceAsync(
        IReadOnlyList<DaemonAssignment> assignments,
        Dictionary<string, List<LiveNode>> candidatesByDaemon,
        Dictionary<Guid, int> load,
        CancellationToken cancellationToken)
    {
        if (assignments.Any(a => a.State != DaemonAssignmentState.Active))
        {
            return;
        }

        var byBusiestNode = assignments
            .OrderByDescending(a => load.GetValueOrDefault(a.AssignedNodeId))
            .ThenBy(a => a.AssignedNodeId)
            .ThenBy(a => a.DaemonId, StringComparer.Ordinal);

        foreach (var assignment in byBusiestNode)
        {
            // A daemon no live node declares is kept where it is, on a node judged dead that could not be removed
            var targets = candidatesByDaemon.GetValueOrDefault(assignment.DaemonId, [])
                .Where(n => n.IsResponsive && !n.GaveUpADaemon && n.NodeId != assignment.AssignedNodeId)
                .ToList();
            if (targets is [])
            {
                continue;
            }

            var target = PickNode(targets, load);
            if (load.GetValueOrDefault(assignment.AssignedNodeId) - load.GetValueOrDefault(target) < 2)
            {
                continue;
            }

            if (await RevokeAsync(assignment, cancellationToken).ConfigureAwait(false))
            {
                logger.LogInformation(
                    "Revoked daemon {DaemonId} from node {NodeId} to balance service {ServiceName}",
                    assignment.DaemonId, assignment.AssignedNodeId, serviceName);
            }

            return;
        }
    }

    /// <summary>
    /// Asks the node running a daemon to stop it and confirm the drain. Nothing is written when the assignment
    /// changed since it was read.
    /// </summary>
    private async Task<bool> RevokeAsync(DaemonAssignment assignment, CancellationToken cancellationToken)
    {
        var revoked = await QueryAsync(
            t => assignmentPersistence.RevokeAsync(serviceName, assignment.DaemonId, assignment.Generation, t),
            cancellationToken).ConfigureAwait(false);
        if (revoked is null)
        {
            logger.LogDebug("Assignment of daemon {DaemonId} changed since it was read; skipped", assignment.DaemonId);
            return false;
        }

        observerInvoker.OnDaemonRevoked(assignment.DaemonId, assignment.AssignedNodeId);
        return true;
    }

    /// <summary>
    /// Removes the assignment of a daemon no live node declares. Nothing is written when the assignment changed
    /// since it was read.
    /// </summary>
    private async Task RemoveAsync(DaemonAssignment assignment, Dictionary<Guid, int> load, CancellationToken cancellationToken)
    {
        var removed = await QueryAsync(
            t => assignmentPersistence.RemoveAsync(serviceName, assignment.DaemonId, assignment.Generation, t),
            cancellationToken).ConfigureAwait(false);
        if (removed)
        {
            load[assignment.AssignedNodeId]--;
            logger.LogInformation(
                "Removed assignment of daemon {DaemonId}: no live node of service {ServiceName} runs it",
                assignment.DaemonId, serviceName);
        }
    }

    /// <summary>
    /// Removes the registrations of nodes this leader saw go unrenewed for their TTL, each only if it was not
    /// renewed since it was read, and returns the nodes removed.
    /// </summary>
    private async Task<IReadOnlySet<Guid>> RemoveDeadNodesAsync(IReadOnlyCollection<NodeRegistration> deadNodes, CancellationToken cancellationToken)
    {
        if (deadNodes.Count == 0)
        {
            return new HashSet<Guid>();
        }

        try
        {
            var removed = await QueryAsync(
                t => leaderElectionPersistence.RemoveNodesAsync(serviceName, deadNodes, t), cancellationToken).ConfigureAwait(false);
            foreach (var nodeId in removed)
            {
                logger.LogInformation("Removed dead node {RemovedNodeId}", nodeId);
                leaderElectionObserverInvoker.OnNodeRemoved(nodeId);
            }

            return removed.ToHashSet();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to remove dead nodes of service {ServiceName}", serviceName);
            return new HashSet<Guid>();
        }
    }

    /// <summary>
    /// Assigns a daemon to the least busy responsive node declaring it, or to the least busy declaring node
    /// when none is responsive. Nothing is written when the assignment changed since it was read.
    /// </summary>
    private async Task AssignAsync(
        string daemonId,
        IReadOnlyList<LiveNode> candidates,
        Dictionary<Guid, int> load,
        DaemonAssignment? expected,
        CancellationToken cancellationToken)
    {
        // A node that did not acknowledge in time gets the daemon back only when no other node can take it,
        // or the daemon could be handed back to it forever
        if (expected is not null && candidates.Where(n => n.NodeId != expected.AssignedNodeId).ToList() is { Count: > 0 } others)
        {
            candidates = others;
        }

        // A node that gave up a daemon it ran may be unable to run any; it gets one only when no other node can
        if (candidates.Where(n => !n.GaveUpADaemon).ToList() is { Count: > 0 } healthy)
        {
            candidates = healthy;
        }

        var responsive = candidates.Where(n => n.IsResponsive).ToList();
        var targetNode = PickNode(responsive is [] ? candidates : responsive, load);

        var assigned = await QueryAsync(
            t => assignmentPersistence.AssignAsync(serviceName, daemonId, targetNode, expected, t),
            cancellationToken).ConfigureAwait(false);
        if (assigned is null)
        {
            logger.LogDebug("Assignment of daemon {DaemonId} changed since it was read; skipped", daemonId);
            return;
        }

        if (expected is not null && load.ContainsKey(expected.AssignedNodeId))
        {
            load[expected.AssignedNodeId]--;
        }

        load[targetNode] = load.GetValueOrDefault(targetNode) + 1;

        logger.LogInformation(
            "Assigned daemon {DaemonId} of service {ServiceName} to node {NodeId} (generation={Generation})",
            daemonId, serviceName, targetNode, assigned.Generation);
        observerInvoker.OnDaemonAssigned(daemonId, targetNode);
    }

    // --- Node: running assignments ---

    private async Task ProcessNodeAssignmentsAsync(
        Guid nodeId,
        Func<LeaseTokenSource> holdLease,
        CancellationToken stoppingToken)
    {
        try
        {
            ReapEndedRuns(nodeId);

            // Daemons cancelled through the worker token (an expired lease) are stopped, not failed
            foreach (var daemonId in runningDaemons.Where(r => r.Value.StopRequested).Select(r => r.Key).ToList())
            {
                StopDaemon(daemonId, nodeId);
            }

            var assignments = await QueryAsync(
                token => assignmentPersistence.GetNodeAssignmentsAsync(serviceName, nodeId, token), stoppingToken).ConfigureAwait(false);

            // The node holds its lease only once it has read its assignments and stopped the runs they no longer
            // allow, before any write that may run out of time: a daemon revoked or moved before the heartbeat is
            // then stopped, and the leader can tell when the node's runs have certainly ended
            StopRunsNoLongerAssigned(assignments, nodeId);
            var lease = holdLease();

            // Acknowledgements first, all at once: the leader moves a daemon its node does not acknowledge in time,
            // and the writes for other assignments could otherwise use up the tick
            var acknowledged = await AcknowledgeAsync(assignments, stoppingToken).ConfigureAwait(false);

            foreach (var assignment in assignments)
            {
                try
                {
                    if (await ReleaseOnceStoppedAsync(assignment, nodeId, stoppingToken).ConfigureAwait(false))
                    {
                        continue;
                    }

                    switch (assignment.State)
                    {
                        case DaemonAssignmentState.Assigning when acknowledged.Contains(assignment.DaemonId):
                            HandleAcknowledged(assignment, nodeId, lease);
                            break;

                        case DaemonAssignmentState.Active:
                            HandleActive(assignment, nodeId, lease);
                            break;

                        case DaemonAssignmentState.Revoking:
                            await HandleRevokingAsync(assignment, nodeId, stoppingToken).ConfigureAwait(false);
                            break;
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // One assignment's failure does not hold up the node's other daemons
                    logger.LogWarning(ex, "Failed to process the assignment of daemon {DaemonId}", assignment.DaemonId);
                }
            }

        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to process node assignments");
        }
    }

    /// <summary>
    /// Stops the runs of daemons no longer assigned to this node, revoked from it, or assigned to it in another
    /// generation, and forgets releases of assignments that are gone.
    /// </summary>
    private void StopRunsNoLongerAssigned(IReadOnlyList<DaemonAssignment> assignments, Guid nodeId)
    {
        var assigned = assignments.ToDictionary(a => a.DaemonId, StringComparer.Ordinal);
        foreach (var daemonId in releaseOnceStopped.Keys.Where(id => !assigned.ContainsKey(id)).ToList())
        {
            releaseOnceStopped.Remove(daemonId);
        }

        foreach (var (daemonId, running) in runningDaemons.ToList())
        {
            if (!assigned.TryGetValue(daemonId, out var assignment))
            {
                StopDaemon(daemonId, nodeId);
                logger.LogInformation("Stopped orphaned daemon {DaemonId}", daemonId);
            }
            else if (assignment.State == DaemonAssignmentState.Revoking || assignment.Generation != running.Generation)
            {
                StopDaemon(daemonId, nodeId);
            }
        }
    }

    /// <summary>
    /// Completes the stop of cancelled runs that ended, and records a failure for every daemon whose run
    /// ended without being asked to stop.
    /// </summary>
    private void ReapEndedRuns(Guid nodeId)
    {
        foreach (var daemonId in stoppingRuns.Keys.ToList())
        {
            _ = HasStoppingRun(daemonId);
        }

        foreach (var (daemonId, running) in runningDaemons.ToList())
        {
            if (!running.Run.IsCompleted || running.StopRequested)
            {
                continue;
            }

            runningDaemons.Remove(daemonId);
            runSlots[daemonId].Current = null;
            running.WorkerLink.Unregister();
            running.Cts.Dispose();

            if (running.Run.Exception is { } exception)
            {
                logger.LogError(exception.GetBaseException(), "Daemon {DaemonId} failed", daemonId);
            }
            else if (CancellationOf(running.Run) is { } cancellation)
            {
                // Cancelled by a token of its own, such as an operation that timed out
                logger.LogError(cancellation, "Daemon {DaemonId} was cancelled although it was not asked to stop", daemonId);
            }
            else
            {
                logger.LogError("Daemon {DaemonId} ended although it was not asked to stop", daemonId);
            }

            observerInvoker.OnDaemonStopped(daemonId, nodeId);
            metrics.RecordFailure(daemonId);
            RecordFailure(daemonId, timeProvider.GetElapsedTime(running.StartedAt));
        }
    }

    /// <summary>
    /// Returns the exception a cancelled run ended with, which a cancelled task keeps, or <c>null</c> when the run
    /// was not cancelled.
    /// </summary>
    private static OperationCanceledException? CancellationOf(Task run)
    {
        if (!run.IsCanceled)
        {
            return null;
        }

        try
        {
            run.GetAwaiter().GetResult();
            return null;
        }
        catch (OperationCanceledException ex)
        {
            return ex;
        }
    }

    /// <summary>
    /// Counts a failed run. Failures are consecutive unless a run lasted the failure cooldown, so a daemon
    /// that keeps failing after a while is caught as surely as one that fails at once.
    /// </summary>
    private void RecordFailure(string daemonId, TimeSpan runLasted)
    {
        var failures = runLasted >= options.FailureCooldown ? 1 : consecutiveFailures.GetValueOrDefault(daemonId) + 1;
        if (failures < options.MaxConsecutiveFailures)
        {
            consecutiveFailures[daemonId] = failures;
            return;
        }

        consecutiveFailures.Remove(daemonId);
        cooldownStartedAt[daemonId] = timeProvider.GetTimestamp();
        logger.LogWarning(
            "Daemon {DaemonId} failed {Failures} times in a row; this node stops running it for {FailureCooldown} while another node offers it",
            daemonId, failures, options.FailureCooldown);
    }

    /// <summary>
    /// Returns whether the node may run the daemon now: it declared it in its last heartbeat and has not given
    /// up on it since.
    /// </summary>
    private bool IsOffered(string daemonId) =>
        declaredDaemonIds.Contains(daemonId) && !IsCoolingDown(daemonId);

    /// <summary>
    /// Returns whether a cancelled run of the daemon has outlived the drain timeout, which every run is expected to
    /// end within. It is reported once.
    /// </summary>
    private bool IsStuckStopping(string daemonId)
    {
        if (!stoppingRuns.TryGetValue(daemonId, out var stopping)
            || stopping.Run.IsCompleted
            || timeProvider.GetElapsedTime(stopping.StoppedAt) < options.DrainTimeout)
        {
            return false;
        }

        if (!stopping.ReportedStuck)
        {
            stopping.ReportedStuck = true;
            logger.LogError(
                "Daemon {DaemonId} did not stop within {DrainTimeout} of being cancelled; this node offers it again only once that run ends",
                daemonId, options.DrainTimeout);
        }

        return true;
    }

    /// <summary>
    /// Returns whether the node gives the daemon up after its repeated failures: for the failure cooldown, and
    /// only while another node of the service offers it.
    /// </summary>
    private bool IsCoolingDown(string daemonId)
    {
        if (!cooldownStartedAt.TryGetValue(daemonId, out var startedAt))
        {
            return false;
        }

        if (timeProvider.GetElapsedTime(startedAt) < options.FailureCooldown)
        {
            // Giving the daemon up only helps when another node can run it; with none, it would only stop it
            return offeredByOthers.Contains(daemonId);
        }

        cooldownStartedAt.Remove(daemonId);
        return false;
    }

    /// <summary>
    /// Acknowledges, all at once, the assignments of daemons this node offers, and returns the identifiers of those
    /// acknowledged. One left unacknowledged is moved to another node by the leader.
    /// </summary>
    private async Task<IReadOnlySet<string>> AcknowledgeAsync(IReadOnlyList<DaemonAssignment> assignments, CancellationToken stoppingToken)
    {
        var acknowledging = assignments
            .Where(a => a.State == DaemonAssignmentState.Assigning
                && FindDaemon(a.DaemonId) is not null
                && IsOffered(a.DaemonId)
                && !(releaseOnceStopped.TryGetValue(a.DaemonId, out var kept) && kept == a.Generation))
            .ToList();
        var results = await Task.WhenAll(acknowledging.Select(async assignment =>
        {
            try
            {
                return await QueryAsync(
                    token => assignmentPersistence.AcknowledgeAsync(serviceName, assignment.DaemonId, assignment.Generation, token),
                    stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Failed to acknowledge daemon {DaemonId}", assignment.DaemonId);
                return false;
            }
        })).ConfigureAwait(false);

        HashSet<string> acknowledged = new(StringComparer.Ordinal);
        for (var i = 0; i < acknowledging.Count; i++)
        {
            var assignment = acknowledging[i];
            if (results[i])
            {
                acknowledged.Add(assignment.DaemonId);
                logger.LogInformation("Acknowledged daemon {DaemonId} assignment (generation={Generation})",
                    assignment.DaemonId, assignment.Generation);
            }
            else
            {
                logger.LogDebug("Did not acknowledge daemon {DaemonId} (generation {Generation} is stale)",
                    assignment.DaemonId, assignment.Generation);
            }
        }

        return acknowledged;
    }

    /// <summary>
    /// Starts the daemon of an assignment this node acknowledged, once a run of an earlier generation has ended.
    /// </summary>
    private void HandleAcknowledged(DaemonAssignment assignment, Guid nodeId, LeaseTokenSource lease)
    {
        StopDaemon(assignment.DaemonId, nodeId);
        if (!HasStoppingRun(assignment.DaemonId))
        {
            StartDaemon(FindDaemon(assignment.DaemonId)!, assignment, nodeId, lease);
        }
    }

    private void HandleActive(DaemonAssignment assignment, Guid nodeId, LeaseTokenSource lease)
    {
        if (runningDaemons.TryGetValue(assignment.DaemonId, out var running))
        {
            if (running.Generation == assignment.Generation)
            {
                return;
            }

            StopDaemon(assignment.DaemonId, nodeId);
        }

        // Active assignment but daemon not running locally: restart (after a failure, an expired lease or a new generation)
        var agent = FindDaemon(assignment.DaemonId);
        if (agent is null
            || !IsOffered(assignment.DaemonId)
            || HasStoppingRun(assignment.DaemonId))
        {
            return;
        }

        logger.LogInformation("Restarting daemon {DaemonId} (active assignment, generation={Generation})",
            assignment.DaemonId, assignment.Generation);

        StartDaemon(agent, assignment, nodeId, lease);
    }

    private async Task HandleRevokingAsync(
        DaemonAssignment assignment,
        Guid nodeId,
        CancellationToken stoppingToken)
    {
        StopDaemon(assignment.DaemonId, nodeId);
        if (HasStoppingRun(assignment.DaemonId))
        {
            // Confirming would let the leader start the daemon elsewhere while it still runs here
            return;
        }

        var confirmed = await QueryAsync(
            token => assignmentPersistence.ConfirmDrainAsync(serviceName, assignment.DaemonId, assignment.Generation, token),
            stoppingToken).ConfigureAwait(false);

        if (confirmed)
        {
            logger.LogInformation("Confirmed drain for daemon {DaemonId} (generation={Generation})",
                assignment.DaemonId, assignment.Generation);
        }
        else
        {
            logger.LogDebug("Failed to confirm drain for daemon {DaemonId} (generation {Generation} is stale)",
                assignment.DaemonId, assignment.Generation);
        }
    }

    private void StartDaemon(IDaemonAgent agent, DaemonAssignment assignment, Guid nodeId, LeaseTokenSource lease)
    {
        if (lease.IsCancellationRequested)
        {
            // The lease ran out, possibly before the timers noticed, as after a pause; the node starts daemons
            // again after its next successful heartbeat
            return;
        }

        // The run is cancelled with the lease, but on the run scheduler: cancelling runs the daemon's callbacks and
        // continuations, and one daemon that blocks there must not delay another one's stop
        var workerToken = lease.Token;
        var cts = new CancellationTokenSource();
        var stop = new RunStop();
        var workerLink = workerToken.Register(
            static state =>
            {
                var (coordinator, source) = ((DaemonCoordinator, CancellationTokenSource))state!;
                coordinator.CancelRun(source);
            },
            (this, cts));

        // The run starts away from the heartbeat, so an agent that blocks before returning its task cannot hold it up
        var run = Task.Factory.StartNew(
            () => agent.RunAsync(new DaemonRunContext(
                cts.Token,
                assignment.Generation,
                assignment.FencingKey,
                () => !stop.IsRequested && !lease.IsCancellationRequested)),
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            runScheduler).Unwrap();

        var running = new RunningDaemon(cts, lease, workerLink, run, assignment.Generation, timeProvider.GetTimestamp(), stop);
        runningDaemons[assignment.DaemonId] = running;
        runSlots[assignment.DaemonId].Current = new ObservedRun(run, running);

        logger.LogInformation("Started daemon {DaemonId} on node {NodeId} (generation={Generation})",
            assignment.DaemonId, nodeId, assignment.Generation);

        metrics.RecordStart(assignment.DaemonId);
        observerInvoker.OnDaemonStarted(assignment.DaemonId, nodeId);
    }

    /// <summary>
    /// Cancels a running daemon without waiting for it. Its run is tracked until it ends.
    /// </summary>
    private void StopDaemon(string daemonId, Guid? nodeId)
    {
        if (!runningDaemons.Remove(daemonId, out var running))
        {
            return;
        }

        // The run stops holding its assignment at once; its token is cancelled on the run scheduler
        running.Stop.Request();
        running.WorkerLink.Unregister();
        CancelRun(running.Cts);
        stoppingRuns[daemonId] = new StoppingRun(running.Cts, running.Run, nodeId, timeProvider.GetTimestamp());
        runSlots[daemonId].Current = new ObservedRun(running.Run, Running: null);
    }

    /// <summary>
    /// Handles an assignment kept through a release because its run had not ended. Returns <c>true</c> while
    /// that generation is still the node's: it is released once the run ends, and never restarted.
    /// </summary>
    private async Task<bool> ReleaseOnceStoppedAsync(DaemonAssignment assignment, Guid nodeId, CancellationToken stoppingToken)
    {
        if (!releaseOnceStopped.TryGetValue(assignment.DaemonId, out var generation))
        {
            return false;
        }

        if (generation != assignment.Generation)
        {
            // The leader assigned a new generation since, a decision taken after the release
            releaseOnceStopped.Remove(assignment.DaemonId);
            return false;
        }

        if (HasStoppingRun(assignment.DaemonId))
        {
            return true;
        }

        // Kept until the release returns: a failed call is retried on the next heartbeat, never restarted
        var released = await QueryAsync(
            token => assignmentPersistence.RemoveAsync(serviceName, assignment.DaemonId, assignment.Generation, token),
            stoppingToken).ConfigureAwait(false);
        releaseOnceStopped.Remove(assignment.DaemonId);
        if (released)
        {
            logger.LogInformation("Node {NodeId} released daemon {DaemonId} once its run ended", nodeId, assignment.DaemonId);
        }

        return true;
    }

    /// <summary>
    /// Cancels a run on the run scheduler, away from the heartbeat and from other runs.
    /// </summary>
    private void CancelRun(CancellationTokenSource run) =>
        _ = Task.Factory.StartNew(
            static state =>
            {
                var (coordinator, source) = ((DaemonCoordinator, CancellationTokenSource))state!;
                try
                {
                    source.Cancel();
                }
                catch (AggregateException ex)
                {
                    coordinator.logger.LogWarning(ex, "A cancellation callback of a daemon failed");
                }
                catch (ObjectDisposedException)
                {
                    // The run ended and was cleaned up before its cancellation ran
                }
            },
            (this, run),
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            runScheduler);

    /// <summary>
    /// Returns whether a cancelled run of the daemon is still going, completing the stop of one that ended.
    /// </summary>
    private bool HasStoppingRun(string daemonId)
    {
        if (!stoppingRuns.TryGetValue(daemonId, out var stopping))
        {
            return false;
        }

        if (!stopping.Run.IsCompleted)
        {
            return true;
        }

        stoppingRuns.Remove(daemonId);
        stopping.Cts.Dispose();
        if (runSlots[daemonId].Current?.Run == stopping.Run)
        {
            runSlots[daemonId].Current = null;
        }

        if (stopping.Run.Exception is { } exception)
        {
            logger.LogWarning(exception.GetBaseException(), "Daemon {DaemonId} failed while stopping", daemonId);
        }

        if (stopping.NodeId is { } stoppedOn)
        {
            observerInvoker.OnDaemonStopped(daemonId, stoppedOn);
        }

        return false;
    }

    /// <summary>
    /// Runs one database operation, bounded by <see cref="LeaderElectionOptions.QueryTimeout"/>.
    /// </summary>
    private async Task<T> QueryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(leaderElectionOptions.QueryTimeout, timeProvider);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        // Also when the client does not honour cancellation: what the abandoned call does late is a late write
        return await operation(bounded.Token).WaitAsync(bounded.Token).ConfigureAwait(false);
    }

    private IDaemonAgent? FindDaemon(string daemonId) =>
        daemons.GetValueOrDefault(daemonId);

    /// <summary>
    /// Indexes the daemons by identifier, reading each identifier once.
    /// </summary>
    private static Dictionary<string, IDaemonAgent> IndexDaemons(IEnumerable<IDaemonAgent> daemons)
    {
        Dictionary<string, IDaemonAgent> byId = new(StringComparer.Ordinal);
        foreach (var daemon in daemons)
        {
            var daemonId = daemon.DaemonId;
            if (string.IsNullOrWhiteSpace(daemonId))
            {
                throw new ArgumentException($"Daemon {daemon.GetType().Name} has no identifier.", nameof(daemons));
            }

            if (daemonId.Length > ValidationConstants.MaxDaemonIdLength)
            {
                throw new ArgumentException(
                    $"Daemon identifier '{daemonId}' is longer than {ValidationConstants.MaxDaemonIdLength} characters.",
                    nameof(daemons));
            }

            if (daemonId.StartsWith(ValidationConstants.ReservedDaemonIdPrefix, StringComparison.OrdinalIgnoreCase) && daemon is not OutboxDaemon)
            {
                throw new ArgumentException(
                    $"Daemon identifier '{daemonId}' starts with '{ValidationConstants.ReservedDaemonIdPrefix}', which is reserved for Emit's own daemons.",
                    nameof(daemons));
            }

            if (!byId.TryAdd(daemonId, daemon))
            {
                throw new ArgumentException($"More than one daemon has the identifier '{daemonId}'.", nameof(daemons));
            }
        }

        return byId;
    }

    /// <summary>
    /// Counts each daemon's runs by state, for the metrics. Called on the exporter's thread: it reads only the run
    /// slots and the thread-safe state of the runs. Reading whether a lease ran out may cancel it from that thread,
    /// which fences the runs under it as the lease's own timer would.
    /// </summary>
    private IReadOnlyList<DaemonRunCount> CountRuns() =>
    [
        .. runSlots.Select(slot => slot.Value.Current switch
        {
            null or { Run.IsCompleted: true } => new DaemonRunCount(slot.Key, Running: 0, Stopping: 0),
            { Running.StopRequested: false } => new DaemonRunCount(slot.Key, Running: 1, Stopping: 0),
            _ => new DaemonRunCount(slot.Key, Running: 0, Stopping: 1),
        }),
    ];

    private static Guid PickNode(IReadOnlyList<LiveNode> candidates, Dictionary<Guid, int> load) =>
        candidates
            .Select(n => n.NodeId)
            .OrderBy(nodeId => load.GetValueOrDefault(nodeId))
            .ThenBy(nodeId => nodeId)
            .First();

    private sealed record RunningDaemon(
        CancellationTokenSource Cts,
        LeaseTokenSource Lease,
        CancellationTokenRegistration WorkerLink,
        Task Run,
        long Generation,
        long StartedAt,
        RunStop Stop)
    {
        /// <summary>
        /// Gets a value indicating whether the run was asked to stop, directly or by fencing the node. The lease is
        /// read rather than its token, which is cancelled later on the callback scheduler: a run that ended because
        /// it no longer held its assignment stopped as asked, not failed.
        /// </summary>
        public bool StopRequested => Cts.IsCancellationRequested || Lease.IsCancellationRequested;
    }

    /// <summary>
    /// Whether a run was asked to stop, set at once, before its token is cancelled on the run scheduler.
    /// </summary>
    private sealed class RunStop
    {
        private volatile bool requested;

        public bool IsRequested => requested;

        public void Request() => requested = true;
    }

    /// <summary>
    /// The latest run of a daemon, as the metrics see it: <see cref="ObservedRun.Running"/> is set while the run was
    /// not asked to stop by this node.
    /// </summary>
    private sealed class RunSlot
    {
        private volatile ObservedRun? current;

        public ObservedRun? Current
        {
            get => current;
            set => current = value;
        }
    }

    private sealed record ObservedRun(Task Run, RunningDaemon? Running);

    private sealed record StoppingRun(CancellationTokenSource Cts, Task Run, Guid? NodeId, long StoppedAt)
    {
        /// <summary>
        /// Gets or sets a value indicating whether the run was reported as outliving the drain timeout.
        /// </summary>
        public bool ReportedStuck { get; set; }
    }

    private enum PlacementAction
    {
        Keep,
        Assign,
        Revoke,
        Remove,
    }

    private readonly record struct PendingAssignment(string DaemonId, long Generation, DaemonAssignmentState State)
    {
        public static PendingAssignment Of(DaemonAssignment assignment) =>
            new(assignment.DaemonId, assignment.Generation, assignment.State);
    }
}

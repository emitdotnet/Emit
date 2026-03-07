namespace Emit.Daemon;

using Emit.Abstractions.Daemon;
using Emit.Abstractions.LeaderElection;
using Emit.Configuration;
using Emit.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Orchestrates daemon assignment on each heartbeat tick. Called by HeartbeatWorker.
/// </summary>
internal sealed class DaemonCoordinator
{
    private readonly IDaemonAssignmentPersistence assignmentPersistence;
    private readonly ILeaderElectionPersistence leaderElectionPersistence;
    private readonly IDaemonAgent[] daemons;
    private readonly DaemonObserverInvoker observerInvoker;
    private readonly DaemonOptions options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<DaemonCoordinator> logger;

    private readonly Dictionary<string, RunningDaemon> runningDaemons = new();

    public DaemonCoordinator(
        IDaemonAssignmentPersistence assignmentPersistence,
        ILeaderElectionPersistence leaderElectionPersistence,
        IEnumerable<IDaemonAgent> daemons,
        DaemonObserverInvoker observerInvoker,
        IOptions<DaemonOptions> options,
        TimeProvider timeProvider,
        ILogger<DaemonCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(assignmentPersistence);
        ArgumentNullException.ThrowIfNull(leaderElectionPersistence);
        ArgumentNullException.ThrowIfNull(daemons);
        ArgumentNullException.ThrowIfNull(observerInvoker);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.assignmentPersistence = assignmentPersistence;
        this.leaderElectionPersistence = leaderElectionPersistence;
        this.daemons = daemons.ToArray();
        this.observerInvoker = observerInvoker;
        this.options = options.Value;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <summary>
    /// Called by HeartbeatWorker on each successful heartbeat tick.
    /// </summary>
    public async Task OnHeartbeatTickAsync(
        Guid nodeId,
        bool isLeader,
        CancellationToken workerToken,
        CancellationToken stoppingToken)
    {
        if (daemons is [])
        {
            return;
        }

        if (isLeader)
        {
            await ReconcileAsync(stoppingToken).ConfigureAwait(false);
        }

        await ProcessNodeAssignmentsAsync(nodeId, workerToken, stoppingToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops all running daemons. Called during application shutdown.
    /// </summary>
    public async Task StopAllDaemonsAsync(CancellationToken cancellationToken)
    {
        foreach (var (daemonId, running) in runningDaemons)
        {
            try
            {
                await running.Cts.CancelAsync().ConfigureAwait(false);
                await running.Agent.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Error stopping daemon {DaemonId} during shutdown", daemonId);
            }
            finally
            {
                running.Cts.Dispose();
            }
        }

        runningDaemons.Clear();
    }

    private async Task ReconcileAsync(CancellationToken stoppingToken)
    {
        try
        {
            var allAssignments = await assignmentPersistence.GetAllAssignmentsAsync(stoppingToken).ConfigureAwait(false);
            var liveNodeIds = await leaderElectionPersistence.GetActiveNodeIdsAsync(stoppingToken).ConfigureAwait(false);

            var assignmentMap = allAssignments.ToDictionary(a => a.DaemonId);
            var liveNodeSet = liveNodeIds.ToHashSet();
            var utcNow = timeProvider.GetUtcNow().UtcDateTime;

            foreach (var daemon in daemons)
            {
                if (!assignmentMap.TryGetValue(daemon.DaemonId, out var assignment))
                {
                    // Unassigned — assign to least-loaded live node
                    var targetNode = PickNode(liveNodeIds, allAssignments);
                    if (targetNode == Guid.Empty)
                    {
                        logger.LogWarning("No live nodes available to assign daemon {DaemonId}", daemon.DaemonId);
                        continue;
                    }

                    var created = await assignmentPersistence.AssignAsync(daemon.DaemonId, targetNode, stoppingToken).ConfigureAwait(false);
                    logger.LogInformation("Assigned daemon {DaemonId} to node {NodeId} (generation={Generation})",
                        daemon.DaemonId, targetNode, created.Generation);
                    await observerInvoker.OnDaemonAssignedAsync(daemon.DaemonId, targetNode, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // Check if assigned to a dead node
                if (!liveNodeSet.Contains(assignment.AssignedNodeId))
                {
                    var targetNode = PickNode(liveNodeIds, allAssignments);
                    if (targetNode == Guid.Empty)
                    {
                        logger.LogWarning("No live nodes available to reassign daemon {DaemonId} from dead node {DeadNodeId}",
                            daemon.DaemonId, assignment.AssignedNodeId);
                        continue;
                    }

                    var reassigned = await assignmentPersistence.AssignAsync(daemon.DaemonId, targetNode, stoppingToken).ConfigureAwait(false);
                    logger.LogInformation("Reassigned daemon {DaemonId} from dead node {DeadNodeId} to {NodeId} (generation={Generation})",
                        daemon.DaemonId, assignment.AssignedNodeId, targetNode, reassigned.Generation);
                    await observerInvoker.OnDaemonAssignedAsync(daemon.DaemonId, targetNode, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // Check for stuck "assigning" state
                if (assignment.State == DaemonAssignmentState.Assigning &&
                    utcNow - assignment.AssignedAt > options.AcknowledgeTimeout)
                {
                    var targetNode = PickNode(liveNodeIds, allAssignments);
                    if (targetNode == Guid.Empty)
                    {
                        continue;
                    }

                    var reassigned = await assignmentPersistence.AssignAsync(daemon.DaemonId, targetNode, stoppingToken).ConfigureAwait(false);
                    logger.LogWarning("Reassigned daemon {DaemonId} stuck in assigning state to {NodeId} (generation={Generation})",
                        daemon.DaemonId, targetNode, reassigned.Generation);
                    await observerInvoker.OnDaemonAssignedAsync(daemon.DaemonId, targetNode, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // Check for past drain deadline
                if (assignment.State == DaemonAssignmentState.Revoking &&
                    assignment.DrainDeadline.HasValue &&
                    utcNow > assignment.DrainDeadline.Value)
                {
                    var targetNode = PickNode(liveNodeIds, allAssignments);
                    if (targetNode == Guid.Empty)
                    {
                        continue;
                    }

                    var reassigned = await assignmentPersistence.AssignAsync(daemon.DaemonId, targetNode, stoppingToken).ConfigureAwait(false);
                    logger.LogWarning("Force-reassigned daemon {DaemonId} past drain deadline to {NodeId} (generation={Generation})",
                        daemon.DaemonId, targetNode, reassigned.Generation);
                    await observerInvoker.OnDaemonAssignedAsync(daemon.DaemonId, targetNode, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to reconcile daemon assignments");
        }
    }

    private async Task ProcessNodeAssignmentsAsync(
        Guid nodeId,
        CancellationToken workerToken,
        CancellationToken stoppingToken)
    {
        try
        {
            // Clean up daemons that were cancelled (e.g., by workerToken cancellation)
            await CleanupCancelledDaemonsAsync(nodeId, stoppingToken).ConfigureAwait(false);

            var assignments = await assignmentPersistence.GetNodeAssignmentsAsync(nodeId, stoppingToken).ConfigureAwait(false);

            foreach (var assignment in assignments)
            {
                switch (assignment.State)
                {
                    case DaemonAssignmentState.Assigning:
                        await HandleAssigningAsync(assignment, nodeId, workerToken, stoppingToken).ConfigureAwait(false);
                        break;

                    case DaemonAssignmentState.Active:
                        await HandleActiveAsync(assignment, nodeId, workerToken).ConfigureAwait(false);
                        break;

                    case DaemonAssignmentState.Revoking:
                        await HandleRevokingAsync(assignment, nodeId, stoppingToken).ConfigureAwait(false);
                        break;
                }
            }

            // Stop daemons not in any assignment (orphaned)
            var assignedIds = assignments.Select(a => a.DaemonId).ToHashSet();
            var orphanedIds = runningDaemons.Keys.Where(id => !assignedIds.Contains(id)).ToList();

            foreach (var daemonId in orphanedIds)
            {
                await StopDaemonAsync(daemonId, nodeId, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to process node assignments");
        }
    }

    private async Task HandleAssigningAsync(
        DaemonAssignment assignment,
        Guid nodeId,
        CancellationToken workerToken,
        CancellationToken stoppingToken)
    {
        var agent = FindDaemon(assignment.DaemonId);
        if (agent is null)
        {
            logger.LogWarning("No daemon agent found for assigned daemon {DaemonId}", assignment.DaemonId);
            return;
        }

        var acknowledged = await assignmentPersistence.AcknowledgeAsync(
            assignment.DaemonId, assignment.Generation, stoppingToken).ConfigureAwait(false);

        if (!acknowledged)
        {
            logger.LogDebug("Failed to acknowledge daemon {DaemonId} (generation {Generation} is stale)",
                assignment.DaemonId, assignment.Generation);
            return;
        }

        logger.LogInformation("Acknowledged daemon {DaemonId} assignment (generation={Generation})",
            assignment.DaemonId, assignment.Generation);

        await StartDaemonAsync(agent, assignment, nodeId, workerToken).ConfigureAwait(false);
    }

    private async Task HandleActiveAsync(
        DaemonAssignment assignment,
        Guid nodeId,
        CancellationToken workerToken)
    {
        if (runningDaemons.ContainsKey(assignment.DaemonId))
        {
            return;
        }

        // Active assignment but daemon not running locally — restart (e.g., after transient heartbeat failure)
        var agent = FindDaemon(assignment.DaemonId);
        if (agent is null)
        {
            logger.LogWarning("No daemon agent found for active daemon {DaemonId}", assignment.DaemonId);
            return;
        }

        logger.LogInformation("Restarting daemon {DaemonId} (active assignment, generation={Generation})",
            assignment.DaemonId, assignment.Generation);

        await StartDaemonAsync(agent, assignment, nodeId, workerToken).ConfigureAwait(false);
    }

    private async Task HandleRevokingAsync(
        DaemonAssignment assignment,
        Guid nodeId,
        CancellationToken stoppingToken)
    {
        if (runningDaemons.TryGetValue(assignment.DaemonId, out var running))
        {
            await running.Cts.CancelAsync().ConfigureAwait(false);

            try
            {
                await running.Agent.StopAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Error stopping daemon {DaemonId} during drain", assignment.DaemonId);
            }

            running.Cts.Dispose();
            runningDaemons.Remove(assignment.DaemonId);

            await observerInvoker.OnDaemonStoppedAsync(assignment.DaemonId, nodeId, stoppingToken).ConfigureAwait(false);
        }

        var confirmed = await assignmentPersistence.ConfirmDrainAsync(
            assignment.DaemonId, assignment.Generation, stoppingToken).ConfigureAwait(false);

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

    private async Task CleanupCancelledDaemonsAsync(Guid nodeId, CancellationToken stoppingToken)
    {
        var cancelledIds = runningDaemons
            .Where(kvp => kvp.Value.Cts.IsCancellationRequested)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var daemonId in cancelledIds)
        {
            var running = runningDaemons[daemonId];

            try
            {
                await running.Agent.StopAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Error stopping cancelled daemon {DaemonId}", daemonId);
            }

            running.Cts.Dispose();
            runningDaemons.Remove(daemonId);

            await observerInvoker.OnDaemonStoppedAsync(daemonId, nodeId, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task StartDaemonAsync(IDaemonAgent agent, DaemonAssignment assignment, Guid nodeId, CancellationToken workerToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(workerToken);

        try
        {
            await agent.StartAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start daemon {DaemonId}", agent.DaemonId);
            cts.Dispose();
            return;
        }

        runningDaemons[agent.DaemonId] = new RunningDaemon(agent, cts, assignment.Generation);

        logger.LogInformation("Started daemon {DaemonId} on node {NodeId} (generation={Generation})",
            agent.DaemonId, nodeId, assignment.Generation);

        await observerInvoker.OnDaemonStartedAsync(agent.DaemonId, nodeId, default).ConfigureAwait(false);
    }

    private async Task StopDaemonAsync(string daemonId, Guid nodeId, CancellationToken stoppingToken)
    {
        if (!runningDaemons.TryGetValue(daemonId, out var running))
        {
            return;
        }

        try
        {
            await running.Cts.CancelAsync().ConfigureAwait(false);
            await running.Agent.StopAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Error stopping orphaned daemon {DaemonId}", daemonId);
        }
        finally
        {
            running.Cts.Dispose();
            runningDaemons.Remove(daemonId);
        }

        logger.LogInformation("Stopped orphaned daemon {DaemonId}", daemonId);
        await observerInvoker.OnDaemonStoppedAsync(daemonId, nodeId, stoppingToken).ConfigureAwait(false);
    }

    private IDaemonAgent? FindDaemon(string daemonId) =>
        daemons.FirstOrDefault(d => d.DaemonId == daemonId);

    private static Guid PickNode(IReadOnlyList<Guid> liveNodeIds, IReadOnlyList<DaemonAssignment> allAssignments)
    {
        if (liveNodeIds is [])
        {
            return Guid.Empty;
        }

        var loadMap = new Dictionary<Guid, int>();
        foreach (var nodeId in liveNodeIds)
        {
            loadMap[nodeId] = 0;
        }

        foreach (var assignment in allAssignments)
        {
            if (loadMap.ContainsKey(assignment.AssignedNodeId))
            {
                loadMap[assignment.AssignedNodeId]++;
            }
        }

        return loadMap.MinBy(kvp => kvp.Value).Key;
    }

    private sealed record RunningDaemon(IDaemonAgent Agent, CancellationTokenSource Cts, long Generation);
}

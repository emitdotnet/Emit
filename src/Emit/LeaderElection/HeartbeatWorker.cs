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
    private readonly ILogger<HeartbeatWorker> logger;

    private CancellationTokenSource? leadershipCts;
    private CancellationTokenSource? workerCts;
    private bool isLeader;
    private bool nodeRegistered;

    public HeartbeatWorker(
        ILeaderElectionPersistence persistence,
        DaemonCoordinator daemonCoordinator,
        LeaderElectionObserverInvoker observerInvoker,
        IOptions<LeaderElectionOptions> options,
        INodeIdentity nodeIdentity,
        ILogger<HeartbeatWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        ArgumentNullException.ThrowIfNull(daemonCoordinator);
        ArgumentNullException.ThrowIfNull(observerInvoker);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(nodeIdentity);
        ArgumentNullException.ThrowIfNull(logger);

        this.persistence = persistence;
        this.daemonCoordinator = daemonCoordinator;
        this.observerInvoker = observerInvoker;
        this.options = options.Value;
        this.logger = logger;

        NodeId = nodeIdentity.NodeId;
    }

    /// <inheritdoc />
    public Guid NodeId { get; }

    /// <inheritdoc />
    public bool IsLeader => isLeader;

    /// <inheritdoc />
    public CancellationToken LeadershipToken => leadershipCts?.Token ?? new CancellationToken(canceled: true);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var instanceId = options.InstanceId ?? Environment.MachineName;

        logger.LogInformation(
            "Leader election starting: nodeId={NodeId}, instanceId={InstanceId}, heartbeatInterval={HeartbeatInterval}, leaseDuration={LeaseDuration}",
            NodeId, instanceId, options.HeartbeatInterval, options.LeaseDuration);

        // Perform an immediate heartbeat before entering the periodic loop
        await PerformHeartbeatAsync(instanceId, stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(options.HeartbeatInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await PerformHeartbeatAsync(instanceId, stoppingToken).ConfigureAwait(false);
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

    private async Task PerformHeartbeatAsync(string instanceId, CancellationToken stoppingToken)
    {
        var request = new HeartbeatRequest(
            NodeId,
            instanceId,
            options.LeaseDuration,
            options.NodeRegistrationTtl);

        try
        {
            using var queryCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            queryCts.CancelAfter(options.QueryTimeout);

            var result = await persistence.HeartbeatAsync(request, queryCts.Token).ConfigureAwait(false);

            // Fire node registered observer on first successful heartbeat
            if (!nodeRegistered)
            {
                nodeRegistered = true;
                logger.LogInformation("Node {NodeId} registered in cluster", NodeId);
                await observerInvoker.OnNodeRegisteredAsync(NodeId, stoppingToken).ConfigureAwait(false);
            }

            // Ensure workerCts is active after a successful heartbeat
            if (workerCts is null || workerCts.IsCancellationRequested)
            {
                workerCts?.Dispose();
                workerCts = new CancellationTokenSource();
            }

            await HandleHeartbeatResultAsync(result, stoppingToken).ConfigureAwait(false);

            logger.LogDebug(
                "Heartbeat succeeded for node {NodeId}, isLeader={IsLeader}, leaderNodeId={LeaderNodeId}",
                NodeId, isLeader, result.LeaderNodeId);

            // Leader-only: clean up dead nodes
            if (isLeader)
            {
                await CleanupDeadNodesAsync(stoppingToken).ConfigureAwait(false);
            }

            // Daemon coordination: reconcile (leader) + process node assignments (all nodes)
            await daemonCoordinator.OnHeartbeatTickAsync(
                NodeId, isLeader, workerCts.Token, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Application is shutting down — let the finally block in ExecuteAsync handle cleanup
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Heartbeat failed for node {NodeId}", NodeId);

            // Cancel workerCts so all daemons stop immediately
            if (workerCts is not null)
            {
                await workerCts.CancelAsync().ConfigureAwait(false);
            }

            if (isLeader)
            {
                logger.LogWarning("Node {NodeId} lost leadership due to heartbeat failure", NodeId);
                DropLeadership();
                await observerInvoker.OnLeaderLostAsync(NodeId, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleHeartbeatResultAsync(HeartbeatResult result, CancellationToken stoppingToken)
    {
        if (result.IsLeader && !isLeader)
        {
            // Became leader
            isLeader = true;
            leadershipCts?.Dispose();
            leadershipCts = new CancellationTokenSource();

            logger.LogInformation("Node {NodeId} elected as leader", NodeId);
            await observerInvoker.OnLeaderElectedAsync(NodeId, stoppingToken).ConfigureAwait(false);
        }
        else if (result.IsLeader && isLeader)
        {
            logger.LogDebug("Node {NodeId} renewed leadership lease", NodeId);
        }
        else if (!result.IsLeader && isLeader)
        {
            // Lost leadership
            DropLeadership();

            logger.LogWarning("Node {NodeId} lost leadership to {LeaderNodeId}", NodeId, result.LeaderNodeId);
            await observerInvoker.OnLeaderLostAsync(NodeId, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task CleanupDeadNodesAsync(CancellationToken stoppingToken)
    {
        try
        {
            var removedNodeIds = await persistence.RemoveExpiredNodesAsync(
                options.NodeRegistrationTtl, stoppingToken).ConfigureAwait(false);

            foreach (var removedNodeId in removedNodeIds)
            {
                logger.LogInformation("Removed dead node {RemovedNodeId}", removedNodeId);
                await observerInvoker.OnNodeRemovedAsync(removedNodeId, stoppingToken).ConfigureAwait(false);
            }

            logger.LogDebug("Dead node cleanup completed, removed {Count} node(s)", removedNodeIds.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to clean up dead nodes");
        }
    }

    private async Task ShutdownAsync()
    {
        logger.LogInformation("Leader election shutting down for node {NodeId}", NodeId);

        try
        {
            // Stop all daemons before resigning leadership
            await daemonCoordinator.StopAllDaemonsAsync(default).ConfigureAwait(false);

            if (isLeader)
            {
                await persistence.ResignLeadershipAsync(NodeId).ConfigureAwait(false);
                logger.LogInformation("Node {NodeId} resigned leadership", NodeId);
            }

            await persistence.DeregisterNodeAsync(NodeId).ConfigureAwait(false);
            logger.LogInformation("Node {NodeId} deregistered", NodeId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Error during leader election shutdown for node {NodeId}", NodeId);
        }
        finally
        {
            DropLeadership();
            workerCts?.Dispose();
            workerCts = null;
        }
    }

    private void DropLeadership()
    {
        isLeader = false;

        if (leadershipCts is not null)
        {
            leadershipCts.Cancel();
            leadershipCts.Dispose();
            leadershipCts = null;
        }
    }
}

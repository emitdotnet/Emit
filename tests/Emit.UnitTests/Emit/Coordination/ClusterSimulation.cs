namespace Emit.UnitTests.Coordination;

using Emit.Abstractions;
using Emit.Abstractions.Daemon;
using Emit.Configuration;
using Emit.Daemon;
using Emit.LeaderElection;
using Emit.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

/// <summary>
/// Runs several nodes of one or more services against a shared <see cref="InMemoryCoordinationStore"/>.
/// Each node is a real <see cref="HeartbeatWorker"/> and <see cref="DaemonCoordinator"/>. Heartbeats are
/// driven explicitly and time comes from a <see cref="FakeTimeProvider"/>, so every scenario is
/// deterministic. Safety invariants are checked after every round.
/// </summary>
internal sealed class ClusterSimulation : IAsyncDisposable
{
    private readonly List<SimNode> nodes = [];

    public ClusterSimulation()
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Store = new InMemoryCoordinationStore(Time);
    }

    public FakeTimeProvider Time { get; }

    public InMemoryCoordinationStore Store { get; }

    public LeaderElectionOptions LeaderElectionOptions { get; } = new()
    {
        HeartbeatInterval = TimeSpan.FromSeconds(1),
        LeaseDuration = TimeSpan.FromSeconds(4),
        QueryTimeout = TimeSpan.FromMilliseconds(150),
        NodeRegistrationTtl = TimeSpan.FromSeconds(7),
    };

    public DaemonOptions DaemonOptions { get; } = new()
    {
        AcknowledgeTimeout = TimeSpan.FromSeconds(3),
        DrainTimeout = TimeSpan.FromMilliseconds(1500),
        MaxConsecutiveFailures = 3,
        FailureCooldown = TimeSpan.FromSeconds(10),
    };

    public IReadOnlyList<SimNode> Nodes => nodes;

    /// <summary>
    /// Creates a node of <paramref name="serviceName"/> able to run <paramref name="daemonIds"/>.
    /// The node does nothing until the next round.
    /// </summary>
    public SimNode StartNode(string serviceName, params string[] daemonIds)
    {
        // Deterministic ids: placement breaks ties by node id, so a seed must replay the same way
        var nodeId = new Guid(nodes.Count + 1, 0, 0, new byte[8]);
        var connection = Store.Connect();
        var identity = new FixedNodeIdentity(nodeId, serviceName);
        var agents = daemonIds.Select(id => new ControllableDaemonAgent(id)).ToList();

        var coordinator = TestNodes.CreateCoordinator(
            connection, connection, agents, DaemonOptions, LeaderElectionOptions, identity, Time, InlineTaskScheduler.Instance);
        var worker = TestNodes.CreateWorker(connection, coordinator, LeaderElectionOptions, identity, Time, InlineTaskScheduler.Instance);

        var node = new SimNode(nodes.Count, serviceName, nodeId, connection, worker, agents);
        nodes.Add(node);
        return node;
    }

    /// <summary>
    /// Advances the clock by one heartbeat interval, then runs one heartbeat on every node that is
    /// running and not frozen, in creation order, and checks the safety invariants.
    /// </summary>
    public async Task RoundAsync()
    {
        Time.Advance(LeaderElectionOptions.HeartbeatInterval);

        foreach (var node in nodes)
        {
            if (node.State != SimNodeState.Running)
            {
                continue;
            }

            if (node.HungRounds > 0)
            {
                node.HungRounds--;
                continue;
            }

            await node.Worker.PerformHeartbeatAsync(CancellationToken.None);
        }

        AssertSafety();
    }

    /// <summary>
    /// Returns how many rounds, one heartbeat interval each, it takes for <paramref name="duration"/> to elapse.
    /// </summary>
    public int RoundsIn(TimeSpan duration) => (int)Math.Ceiling(duration / LeaderElectionOptions.HeartbeatInterval);

    /// <summary>
    /// Runs <paramref name="count"/> rounds.
    /// </summary>
    public async Task RoundsAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await RoundAsync();
        }
    }

    /// <summary>
    /// Runs enough rounds for any expired lease, dead node or pending assignment to be resolved.
    /// </summary>
    public Task SettleAsync()
    {
        var longest = LeaderElectionOptions.NodeRegistrationTtl
            + LeaderElectionOptions.LeaseDuration
            + DaemonOptions.AcknowledgeTimeout
            + DaemonOptions.DrainTimeout;
        var rounds = (int)Math.Ceiling(longest / LeaderElectionOptions.HeartbeatInterval) + 3;
        return RoundsAsync(rounds);
    }

    /// <summary>
    /// Simulates a process crash: the node stops heartbeating and its daemons die with it.
    /// </summary>
    public void Kill(SimNode node)
    {
        node.State = SimNodeState.Killed;
        foreach (var agent in node.Agents)
        {
            agent.ProcessDied();
        }
    }

    /// <summary>
    /// Simulates a graceful shutdown: daemons are stopped, leadership resigned and the node deregistered.
    /// </summary>
    public async Task StopAsync(SimNode node)
    {
        await node.Worker.ShutdownAsync();
        node.State = SimNodeState.Stopped;
    }

    /// <summary>
    /// Asserts the properties that must hold at all times, whatever failures occurred.
    /// </summary>
    public void AssertSafety()
    {
        // A killed process leads nothing, though its lease has not run out; its daemons died with it. A node that
        // was stopped must have stopped its daemons, or they run alongside the ones started elsewhere.
        foreach (var service in nodes.GroupBy(n => n.ServiceName))
        {
            var leaders = service.Where(n => n.State != SimNodeState.Killed && n.Worker.IsLeader).Select(n => n.Index).ToList();
            Assert.True(
                leaders.Count <= 1,
                $"Service '{service.Key}' has {leaders.Count} leaders at {Time.GetUtcNow():O}: nodes {string.Join(", ", leaders)}.");

            var running = service
                .SelectMany(n => n.Agents.Where(a => a.IsRunning).Select(a => (Node: n.Index, a.DaemonId)))
                .GroupBy(r => r.DaemonId);
            foreach (var daemon in running)
            {
                Assert.True(
                    daemon.Count() == 1,
                    $"Daemon '{daemon.Key}' of service '{service.Key}' runs on {daemon.Count()} nodes at {Time.GetUtcNow():O}: " +
                    $"nodes {string.Join(", ", daemon.Select(d => d.Node))}.");
            }
        }

        foreach (var (serviceName, assignment) in Store.GetAssignmentsSnapshot())
        {
            var target = nodes.SingleOrDefault(n => n.NodeId == assignment.AssignedNodeId);
            Assert.True(
                target is null || target.ServiceName == serviceName,
                $"Daemon '{assignment.DaemonId}' of service '{serviceName}' is assigned to node {target?.Index} of service '{target?.ServiceName}'.");
            Assert.True(
                target is null || target.Agents.Any(a => a.DaemonId == assignment.DaemonId),
                $"Daemon '{assignment.DaemonId}' of service '{serviceName}' is assigned to node {target?.Index}, which does not declare it.");
        }
    }

    /// <summary>
    /// Asserts that every service with healthy nodes has exactly one leader, that every daemon declared by a
    /// healthy node runs exactly once in its service, and that a service with healthy nodes keeps no assignment
    /// for a daemon none of its running nodes declares.
    /// </summary>
    public void AssertConverged()
    {
        var healthy = nodes
            .Where(n => n.State == SimNodeState.Running && !n.Connection.IsPartitioned)
            .ToList();

        foreach (var service in healthy.GroupBy(n => n.ServiceName))
        {
            Assert.True(
                service.Count(n => n.Worker.IsLeader) == 1,
                $"Service '{service.Key}' has {service.Count(n => n.Worker.IsLeader)} leaders after settling.");

            foreach (var daemonId in service.SelectMany(n => n.Agents).Select(a => a.DaemonId).Distinct())
            {
                var runners = service.Count(n => n.Agents.Any(a => a.DaemonId == daemonId && a.IsRunning));
                Assert.True(
                    runners == 1,
                    $"Daemon '{daemonId}' of service '{service.Key}' runs on {runners} nodes after settling.");
            }
        }

        // Only a service with a leader cleans up, so a service without healthy nodes keeps its assignments
        var ledServices = healthy.Select(n => n.ServiceName).ToHashSet(StringComparer.Ordinal);
        foreach (var (serviceName, assignment) in Store.GetAssignmentsSnapshot().Where(a => ledServices.Contains(a.ServiceName)))
        {
            Assert.True(
                nodes.Any(n => n.State == SimNodeState.Running
                    && n.ServiceName == serviceName
                    && n.Agents.Any(a => a.DaemonId == assignment.DaemonId)),
                $"Daemon '{assignment.DaemonId}' of service '{serviceName}' is still assigned although no running node declares it.");
        }
    }

    public ValueTask DisposeAsync()
    {
        foreach (var node in nodes)
        {
            node.Worker.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private sealed class FixedNodeIdentity(Guid nodeId, string serviceName) : INodeIdentity
    {
        public Guid NodeId { get; } = nodeId;

        public string ServiceName { get; } = serviceName;
    }
}

/// <summary>
/// Lifecycle state of a simulated node.
/// </summary>
internal enum SimNodeState
{
    Running,
    Killed,
    Stopped,
}

/// <summary>
/// A node in a <see cref="ClusterSimulation"/>.
/// </summary>
internal sealed class SimNode(
    int index,
    string serviceName,
    Guid nodeId,
    InMemoryCoordinationStore.Connection connection,
    HeartbeatWorker worker,
    IReadOnlyList<ControllableDaemonAgent> agents)
{
    public int Index { get; } = index;

    public string ServiceName { get; } = serviceName;

    public Guid NodeId { get; } = nodeId;

    public InMemoryCoordinationStore.Connection Connection { get; } = connection;

    public HeartbeatWorker Worker { get; } = worker;

    public IReadOnlyList<ControllableDaemonAgent> Agents { get; } = agents;

    public SimNodeState State { get; set; } = SimNodeState.Running;

    /// <summary>
    /// Gets or sets the number of upcoming rounds in which the node's heartbeat loop is stuck, for
    /// example on a call that never returns. Its daemons keep running until its lease timer fences them.
    /// </summary>
    public int HungRounds { get; set; }

    public ControllableDaemonAgent Agent(string daemonId) => Agents.Single(a => a.DaemonId == daemonId);

    public bool IsRunning(string daemonId) => Agents.Any(a => a.DaemonId == daemonId && a.IsRunning);
}

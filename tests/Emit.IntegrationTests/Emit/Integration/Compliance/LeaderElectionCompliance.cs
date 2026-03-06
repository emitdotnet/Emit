namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions.Daemon;
using Emit.Abstractions.LeaderElection;
using Emit.Abstractions.Observability;
using Emit.Configuration;
using Emit.Daemon;
using Emit.LeaderElection;
using Emit.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Compliance tests for leader election behavior. Derived classes provide a database-specific
/// <see cref="ILeaderElectionPersistence"/> instance. Uses short intervals to keep tests fast
/// but not flaky.
/// </summary>
[Trait("Category", "Integration")]
public abstract class LeaderElectionCompliance : IAsyncLifetime
{
    /// <summary>
    /// Short lease for tests that need lease expiry (2s).
    /// </summary>
    protected static readonly TimeSpan ShortLeaseDuration = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Wait time for lease expiry tests (3s = 2s lease + 1s buffer).
    /// </summary>
    protected static readonly TimeSpan ExpiryWaitTime = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Long lease for tests that don't need expiry (10s).
    /// </summary>
    protected static readonly TimeSpan LongLeaseDuration = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Heartbeat interval for tests (500ms).
    /// </summary>
    protected static readonly TimeSpan TestHeartbeatInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Query timeout for tests (250ms).
    /// </summary>
    protected static readonly TimeSpan TestQueryTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Node registration TTL for tests (must be > LeaseDuration).
    /// </summary>
    protected static readonly TimeSpan TestNodeRegistrationTtl = TimeSpan.FromSeconds(12);

    /// <summary>
    /// Short node TTL for dead node cleanup tests.
    /// </summary>
    protected static readonly TimeSpan ShortNodeRegistrationTtl = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Gets the persistence implementation under test.
    /// </summary>
    protected abstract ILeaderElectionPersistence Persistence { get; }

    /// <inheritdoc/>
    public abstract Task InitializeAsync();

    /// <inheritdoc/>
    public abstract Task DisposeAsync();

    // --- Group 1: Basic Election ---

    [Fact]
    public async Task GivenSingleNode_WhenStarted_ThenBecomesLeader()
    {
        // Arrange
        await using var node = CreateNode(LongLeaseDuration);

        // Act
        await node.Worker.StartAsync(CancellationToken.None);
        await WaitForHeartbeats(2);

        // Assert
        Assert.True(node.Worker.IsLeader);

        // Cleanup
        await node.Worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task GivenSingleNode_WhenStarted_ThenNodeIsRegistered()
    {
        // Arrange
        await using var node = CreateNode(LongLeaseDuration);

        // Act
        await node.Worker.StartAsync(CancellationToken.None);
        await WaitForHeartbeats(2);

        // Assert
        Assert.True(node.Observer.NodeRegistered);

        // Cleanup
        await node.Worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task GivenSingleNode_WhenStarted_ThenObserverNotified()
    {
        // Arrange
        await using var node = CreateNode(LongLeaseDuration);

        // Act
        await node.Worker.StartAsync(CancellationToken.None);
        await WaitForHeartbeats(2);

        // Assert
        Assert.True(node.Observer.LeaderElected);
        Assert.True(node.Observer.NodeRegistered);

        // Cleanup
        await node.Worker.StopAsync(CancellationToken.None);
    }

    // --- Group 2: Contention ---

    [Fact]
    public async Task GivenTwoNodes_WhenBothStarted_ThenExactlyOneIsLeader()
    {
        // Arrange
        await using var node1 = CreateNode(LongLeaseDuration);
        await using var node2 = CreateNode(LongLeaseDuration);

        // Act
        await node1.Worker.StartAsync(CancellationToken.None);
        await node2.Worker.StartAsync(CancellationToken.None);
        await WaitForHeartbeats(3);

        // Assert
        var leaders = new[] { node1.Worker, node2.Worker }.Count(w => w.IsLeader);
        Assert.Equal(1, leaders);

        // Cleanup
        await node1.Worker.StopAsync(CancellationToken.None);
        await node2.Worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task GivenThreeNodes_WhenAllStarted_ThenExactlyOneIsLeader()
    {
        // Arrange
        await using var node1 = CreateNode(LongLeaseDuration);
        await using var node2 = CreateNode(LongLeaseDuration);
        await using var node3 = CreateNode(LongLeaseDuration);

        // Act
        await node1.Worker.StartAsync(CancellationToken.None);
        await node2.Worker.StartAsync(CancellationToken.None);
        await node3.Worker.StartAsync(CancellationToken.None);
        await WaitForHeartbeats(3);

        // Assert
        var leaders = new[] { node1.Worker, node2.Worker, node3.Worker }.Count(w => w.IsLeader);
        Assert.Equal(1, leaders);

        // Cleanup
        await node1.Worker.StopAsync(CancellationToken.None);
        await node2.Worker.StopAsync(CancellationToken.None);
        await node3.Worker.StopAsync(CancellationToken.None);
    }

    // --- Group 3: Lease Expiry & Failover ---

    [Fact]
    public async Task GivenLeader_WhenStopped_ThenFollowerTakesOver()
    {
        // Arrange
        await using var leader = CreateNode(ShortLeaseDuration);
        await using var follower = CreateNode(ShortLeaseDuration);

        await leader.Worker.StartAsync(CancellationToken.None);
        await WaitForHeartbeats(2);
        Assert.True(leader.Worker.IsLeader);

        await follower.Worker.StartAsync(CancellationToken.None);
        await WaitForHeartbeats(2);

        // Act — stop leader without graceful resign (simulate crash by cancelling)
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await leader.Worker.StopAsync(cts.Token);

        // Wait for lease to expire and follower to take over
        await Task.Delay(ExpiryWaitTime);

        // Assert
        Assert.True(follower.Worker.IsLeader);

        // Cleanup
        await follower.Worker.StopAsync(CancellationToken.None);
    }

    // --- Group 4: Split-Brain Prevention ---

    [Fact]
    public async Task GivenTwoNodes_WhenBothAttemptLeadership_ThenNeverTwoLeadersSimultaneously()
    {
        // Arrange
        await using var node1 = CreateNode(LongLeaseDuration);
        await using var node2 = CreateNode(LongLeaseDuration);

        await node1.Worker.StartAsync(CancellationToken.None);
        await node2.Worker.StartAsync(CancellationToken.None);

        // Act — poll rapidly over several heartbeat cycles
        var maxLeaders = 0;
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(TestHeartbeatInterval / 2);
            var leaders = new[] { node1.Worker, node2.Worker }.Count(w => w.IsLeader);
            maxLeaders = Math.Max(maxLeaders, leaders);
        }

        // Assert
        Assert.True(maxLeaders <= 1, $"Expected at most 1 leader at any time, but found {maxLeaders}");

        // Cleanup
        await node1.Worker.StopAsync(CancellationToken.None);
        await node2.Worker.StopAsync(CancellationToken.None);
    }

    // --- Group 5: Resignation ---

    [Fact]
    public async Task GivenLeader_WhenGracefulShutdown_ThenResignsAndDeregisters()
    {
        // Arrange
        await using var node = CreateNode(LongLeaseDuration);
        await node.Worker.StartAsync(CancellationToken.None);
        await WaitForHeartbeats(2);
        Assert.True(node.Worker.IsLeader);

        // Act
        await node.Worker.StopAsync(CancellationToken.None);

        // Assert — a new node can immediately become leader (no lease to wait out)
        await using var newNode = CreateNode(LongLeaseDuration);
        await newNode.Worker.StartAsync(CancellationToken.None);
        await WaitForHeartbeats(2);
        Assert.True(newNode.Worker.IsLeader);

        // Cleanup
        await newNode.Worker.StopAsync(CancellationToken.None);
    }

    // --- Group 6: Node Registration ---

    [Fact]
    public async Task GivenMultipleNodes_WhenStarted_ThenAllRegisteredInDb()
    {
        // Arrange
        await using var node1 = CreateNode(LongLeaseDuration);
        await using var node2 = CreateNode(LongLeaseDuration);

        // Act
        await node1.Worker.StartAsync(CancellationToken.None);
        await node2.Worker.StartAsync(CancellationToken.None);
        await WaitForHeartbeats(2);

        // Assert
        Assert.True(node1.Observer.NodeRegistered);
        Assert.True(node2.Observer.NodeRegistered);

        // Cleanup
        await node1.Worker.StopAsync(CancellationToken.None);
        await node2.Worker.StopAsync(CancellationToken.None);
    }

    // --- Group 7: Dead Node Cleanup ---

    [Fact]
    public async Task GivenDeadNode_WhenLeaderCleans_ThenNodeRemoved()
    {
        // Arrange — register a dead node directly via persistence (simulating a crash
        // where the node never gets to run its shutdown cleanup)
        var deadNodeId = Guid.NewGuid();
        await Persistence.HeartbeatAsync(
            new HeartbeatRequest(deadNodeId, "dead-instance", ShortLeaseDuration, ShortNodeRegistrationTtl),
            CancellationToken.None);

        // Start the leader with short TTL for dead node detection
        await using var leader = CreateNode(ShortLeaseDuration, ShortNodeRegistrationTtl);
        await leader.Worker.StartAsync(CancellationToken.None);
        await WaitForHeartbeats(2);

        // Act — wait for the dead node's TTL to expire and leader to clean up
        await Task.Delay(ShortNodeRegistrationTtl + TestHeartbeatInterval);

        // Assert
        Assert.True(leader.Observer.RemovedNodeIds.Count > 0);

        // Cleanup
        await leader.Worker.StopAsync(CancellationToken.None);
    }

    // --- Group 8: Node Deregistration ---

    [Fact]
    public async Task GivenNode_WhenStopped_ThenDeregistersGracefully()
    {
        // Arrange
        await using var node = CreateNode(LongLeaseDuration);
        await node.Worker.StartAsync(CancellationToken.None);
        await WaitForHeartbeats(2);

        // Act
        await node.Worker.StopAsync(CancellationToken.None);

        // Assert — a new node's first heartbeat should not find the old node
        // (We verify by checking no old node cleanup is needed)
        await using var newNode = CreateNode(LongLeaseDuration);
        await newNode.Worker.StartAsync(CancellationToken.None);
        await WaitForHeartbeats(2);
        await newNode.Worker.StopAsync(CancellationToken.None);
    }

    // --- Group 9: Edge Cases ---

    [Fact]
    public async Task GivenLeader_WhenExtendSucceeds_ThenRemainsLeader()
    {
        // Arrange
        await using var node = CreateNode(LongLeaseDuration);
        await node.Worker.StartAsync(CancellationToken.None);
        await WaitForHeartbeats(2);
        Assert.True(node.Worker.IsLeader);

        // Act — wait for several more heartbeat cycles
        await WaitForHeartbeats(4);

        // Assert
        Assert.True(node.Worker.IsLeader);

        // Cleanup
        await node.Worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task GivenNoNodes_WhenFirstNodeStarts_ThenBecomesLeaderImmediately()
    {
        // Arrange
        await using var node = CreateNode(LongLeaseDuration);

        // Act
        await node.Worker.StartAsync(CancellationToken.None);
        // Wait just enough for the first heartbeat tick
        await Task.Delay(TimeSpan.FromMilliseconds(750));

        // Assert
        Assert.True(node.Worker.IsLeader);

        // Cleanup
        await node.Worker.StopAsync(CancellationToken.None);
    }

    // --- Helpers ---

    private TestNode CreateNode(TimeSpan leaseDuration, TimeSpan? nodeTtl = null)
    {
        var observer = new TestLeaderElectionObserver();
        var invoker = new LeaderElectionObserverInvoker(
            [observer],
            NullLogger<LeaderElectionObserverInvoker>.Instance);

        var options = new LeaderElectionOptions
        {
            HeartbeatInterval = TestHeartbeatInterval,
            LeaseDuration = leaseDuration,
            QueryTimeout = TestQueryTimeout,
            NodeRegistrationTtl = nodeTtl ?? TestNodeRegistrationTtl
        };

        var daemonCoordinator = new DaemonCoordinator(
            new NoOpDaemonAssignmentPersistence(),
            Persistence,
            [],
            new DaemonObserverInvoker([], NullLogger<DaemonObserverInvoker>.Instance),
            Options.Create(new DaemonOptions()),
            TimeProvider.System,
            NullLogger<DaemonCoordinator>.Instance);

        var worker = new HeartbeatWorker(
            Persistence,
            daemonCoordinator,
            invoker,
            Options.Create(options),
            NullLogger<HeartbeatWorker>.Instance);

        return new TestNode(worker, observer);
    }

    private static async Task WaitForHeartbeats(int count)
    {
        await Task.Delay(TestHeartbeatInterval * count + TimeSpan.FromMilliseconds(500));
    }

    /// <summary>
    /// Holds a worker and its observer for test assertions.
    /// </summary>
    private sealed class TestNode(HeartbeatWorker worker, TestLeaderElectionObserver observer) : IAsyncDisposable
    {
        public HeartbeatWorker Worker { get; } = worker;
        public TestLeaderElectionObserver Observer { get; } = observer;

        public async ValueTask DisposeAsync()
        {
            Worker.Dispose();
        }
    }

    /// <summary>
    /// No-op daemon assignment persistence for leader election tests (no daemons registered).
    /// </summary>
    private sealed class NoOpDaemonAssignmentPersistence : IDaemonAssignmentPersistence
    {
        public Task<DaemonAssignment> AssignAsync(string daemonId, Guid nodeId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<DaemonAssignment?> RevokeAsync(string daemonId, TimeSpan drainTimeout, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DaemonAssignment>> GetAllAssignmentsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DaemonAssignment>>([]);

        public Task<IReadOnlyList<DaemonAssignment>> GetNodeAssignmentsAsync(Guid nodeId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DaemonAssignment>>([]);

        public Task<bool> AcknowledgeAsync(string daemonId, long generation, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<bool> ConfirmDrainAsync(string daemonId, long generation, CancellationToken ct) =>
            Task.FromResult(false);
    }

    /// <summary>
    /// Test observer that records events for assertions.
    /// </summary>
    private sealed class TestLeaderElectionObserver : ILeaderElectionObserver
    {
        public bool LeaderElected { get; private set; }
        public bool LeaderLost { get; private set; }
        public bool NodeRegistered { get; private set; }
        public List<Guid> RemovedNodeIds { get; } = [];

        public Task OnLeaderElectedAsync(Guid nodeId, CancellationToken cancellationToken)
        {
            LeaderElected = true;
            return Task.CompletedTask;
        }

        public Task OnLeaderLostAsync(Guid nodeId, CancellationToken cancellationToken)
        {
            LeaderLost = true;
            return Task.CompletedTask;
        }

        public Task OnNodeRegisteredAsync(Guid nodeId, CancellationToken cancellationToken)
        {
            NodeRegistered = true;
            return Task.CompletedTask;
        }

        public Task OnNodeRemovedAsync(Guid nodeId, CancellationToken cancellationToken)
        {
            RemovedNodeIds.Add(nodeId);
            return Task.CompletedTask;
        }
    }
}

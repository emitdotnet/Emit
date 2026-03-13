namespace Emit.UnitTests.LeaderElection;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Daemon;
using global::Emit.Abstractions.LeaderElection;
using global::Emit.Abstractions.Observability;
using global::Emit.Configuration;
using global::Emit.Daemon;
using global::Emit.LeaderElection;
using global::Emit.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

public class HeartbeatWorkerTests
{
    private readonly Mock<ILeaderElectionPersistence> mockPersistence = new();
    private readonly Mock<ILeaderElectionObserver> mockObserver = new();
    private readonly LeaderElectionOptions options = new()
    {
        HeartbeatInterval = TimeSpan.FromMilliseconds(100),
        LeaseDuration = TimeSpan.FromSeconds(5),
        QueryTimeout = TimeSpan.FromSeconds(1),
        NodeRegistrationTtl = TimeSpan.FromSeconds(10)
    };

    [Fact]
    public async Task GivenHeartbeatReturnsLeader_WhenFirstHeartbeat_ThenBecomesLeaderAndNotifiesObserver()
    {
        // Arrange
        var worker = CreateWorker();

        mockPersistence.Setup(p => p.HeartbeatAsync(
                It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HeartbeatRequest req, CancellationToken _) =>
                new HeartbeatResult(true, req.NodeId));

        mockPersistence.Setup(p => p.RemoveExpiredNodesAsync(
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(300); // Let at least one heartbeat run
        var wasLeader = worker.IsLeader;
        await worker.StopAsync(CancellationToken.None);

        // Assert — check state before graceful shutdown (which resigns leadership)
        Assert.True(wasLeader);
        mockObserver.Verify(o => o.OnLeaderElectedAsync(worker.NodeId, It.IsAny<CancellationToken>()), Times.Once);
        mockObserver.Verify(o => o.OnNodeRegisteredAsync(worker.NodeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenIsLeader_WhenHeartbeatContinuesReturningLeader_ThenNosDuplicateObserverCall()
    {
        // Arrange
        var worker = CreateWorker();

        mockPersistence.Setup(p => p.HeartbeatAsync(
                It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HeartbeatRequest req, CancellationToken _) =>
                new HeartbeatResult(true, req.NodeId));

        mockPersistence.Setup(p => p.RemoveExpiredNodesAsync(
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(500); // Let multiple heartbeats run
        await worker.StopAsync(CancellationToken.None);

        // Assert
        mockObserver.Verify(o => o.OnLeaderElectedAsync(worker.NodeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenIsLeader_WhenHeartbeatFails_ThenDropsLeadershipImmediately()
    {
        // Arrange
        var worker = CreateWorker();
        var heartbeatCallCount = 0;

        mockPersistence.Setup(p => p.HeartbeatAsync(
                It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .Returns((HeartbeatRequest req, CancellationToken _) =>
            {
                heartbeatCallCount++;
                if (heartbeatCallCount <= 1)
                {
                    return Task.FromResult(new HeartbeatResult(true, req.NodeId));
                }

                throw new InvalidOperationException("DB connection lost");
            });

        mockPersistence.Setup(p => p.RemoveExpiredNodesAsync(
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(500); // Let failure happen
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.False(worker.IsLeader);
        mockObserver.Verify(o => o.OnLeaderLostAsync(worker.NodeId, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task GivenIsLeader_WhenHeartbeatReturnsNotLeader_ThenLosesLeadershipAndCancelsToken()
    {
        // Arrange
        var worker = CreateWorker();
        var otherNodeId = Guid.NewGuid();
        var heartbeatCallCount = 0;

        mockPersistence.Setup(p => p.HeartbeatAsync(
                It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .Returns((HeartbeatRequest req, CancellationToken _) =>
            {
                heartbeatCallCount++;
                return heartbeatCallCount <= 1
                    ? Task.FromResult(new HeartbeatResult(true, req.NodeId))
                    : Task.FromResult(new HeartbeatResult(false, otherNodeId));
            });

        mockPersistence.Setup(p => p.RemoveExpiredNodesAsync(
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(500);
        var leadershipToken = worker.LeadershipToken;
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.False(worker.IsLeader);
        Assert.True(leadershipToken.IsCancellationRequested);
        mockObserver.Verify(o => o.OnLeaderLostAsync(worker.NodeId, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task GivenIsLeader_WhenShutdown_ThenResignsAndDeregisters()
    {
        // Arrange
        var worker = CreateWorker();

        mockPersistence.Setup(p => p.HeartbeatAsync(
                It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HeartbeatRequest req, CancellationToken _) =>
                new HeartbeatResult(true, req.NodeId));

        mockPersistence.Setup(p => p.RemoveExpiredNodesAsync(
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        mockPersistence.Verify(p => p.ResignLeadershipAsync(worker.NodeId, It.IsAny<CancellationToken>()), Times.Once);
        mockPersistence.Verify(p => p.DeregisterNodeAsync(worker.NodeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenIsFollower_WhenShutdown_ThenDeregistersWithoutResigning()
    {
        // Arrange
        var worker = CreateWorker();
        var leaderNodeId = Guid.NewGuid();

        mockPersistence.Setup(p => p.HeartbeatAsync(
                It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HeartbeatResult(false, leaderNodeId));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        mockPersistence.Verify(p => p.ResignLeadershipAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        mockPersistence.Verify(p => p.DeregisterNodeAsync(worker.NodeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenIsLeader_WhenRemoveExpiredNodesReturnsIds_ThenFiresOnNodeRemovedForEach()
    {
        // Arrange
        var worker = CreateWorker();
        var removedNodeId1 = Guid.NewGuid();
        var removedNodeId2 = Guid.NewGuid();

        mockPersistence.Setup(p => p.HeartbeatAsync(
                It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HeartbeatRequest req, CancellationToken _) =>
                new HeartbeatResult(true, req.NodeId));

        mockPersistence.Setup(p => p.RemoveExpiredNodesAsync(
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { removedNodeId1, removedNodeId2 });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        mockObserver.Verify(o => o.OnNodeRemovedAsync(removedNodeId1, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        mockObserver.Verify(o => o.OnNodeRemovedAsync(removedNodeId2, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public void GivenNewWorker_WhenNodeId_ThenReturnsNonEmptyGuid()
    {
        // Arrange
        var worker = CreateWorker();

        // Assert
        Assert.NotEqual(Guid.Empty, worker.NodeId);
    }

    [Fact]
    public void GivenInjectedNodeIdentity_WhenNodeId_ThenMatchesInjectedValue()
    {
        // Arrange
        var expectedNodeId = Guid.NewGuid();
        var nodeIdentityMock = new Mock<INodeIdentity>();
        nodeIdentityMock.Setup(n => n.NodeId).Returns(expectedNodeId);

        var worker = CreateWorker(nodeIdentityMock.Object);

        // Assert
        Assert.Equal(expectedNodeId, worker.NodeId);
    }

    [Fact]
    public void GivenNewWorker_WhenIsLeader_ThenReturnsFalse()
    {
        // Arrange
        var worker = CreateWorker();

        // Assert
        Assert.False(worker.IsLeader);
    }

    [Fact]
    public void GivenNewWorker_WhenLeadershipToken_ThenIsCancelled()
    {
        // Arrange
        var worker = CreateWorker();

        // Assert
        Assert.True(worker.LeadershipToken.IsCancellationRequested);
    }

    private HeartbeatWorker CreateWorker(INodeIdentity? nodeIdentity = null)
    {
        var invoker = new LeaderElectionObserverInvoker(
            [mockObserver.Object],
            NullLogger<LeaderElectionObserverInvoker>.Instance);

        var daemonCoordinator = new DaemonCoordinator(
            new Mock<IDaemonAssignmentPersistence>().Object,
            mockPersistence.Object,
            [],
            new DaemonObserverInvoker([], NullLogger<DaemonObserverInvoker>.Instance),
            Options.Create(new DaemonOptions()),
            TimeProvider.System,
            NullLogger<DaemonCoordinator>.Instance);

        if (nodeIdentity is null)
        {
            var nodeIdentityMock = new Mock<INodeIdentity>();
            nodeIdentityMock.Setup(n => n.NodeId).Returns(Guid.NewGuid());
            nodeIdentity = nodeIdentityMock.Object;
        }

        return new HeartbeatWorker(
            mockPersistence.Object,
            daemonCoordinator,
            invoker,
            Options.Create(options),
            nodeIdentity,
            NullLogger<HeartbeatWorker>.Instance);
    }
}

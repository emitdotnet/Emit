namespace Emit.UnitTests.Daemon;

using global::Emit.Abstractions.Daemon;
using global::Emit.Abstractions.LeaderElection;
using global::Emit.Configuration;
using global::Emit.Daemon;
using global::Emit.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

public class DaemonCoordinatorTests
{
    private const string TestDaemonId = "test-daemon";

    private readonly Mock<IDaemonAssignmentPersistence> mockAssignmentPersistence = new();
    private readonly Mock<ILeaderElectionPersistence> mockLeaderElectionPersistence = new();
    private readonly Mock<IDaemonAgent> mockAgent = new();

    private readonly Guid nodeId = Guid.NewGuid();

    public DaemonCoordinatorTests()
    {
        mockAgent.Setup(a => a.DaemonId).Returns(TestDaemonId);
        mockAgent.Setup(a => a.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mockAgent.Setup(a => a.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    // --- Reconciliation (leader=true) ---

    [Fact]
    public async Task GivenUnassignedDaemon_WhenReconcile_ThenAssignsToNode()
    {
        // Arrange
        var newAssignment = MakeAssignment(nodeId, DaemonAssignmentState.Assigning, generation: 1);

        mockAssignmentPersistence
            .Setup(p => p.GetAllAssignmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        mockAssignmentPersistence
            .Setup(p => p.GetNodeAssignmentsAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        mockLeaderElectionPersistence
            .Setup(p => p.GetActiveNodeIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([nodeId]);

        mockAssignmentPersistence
            .Setup(p => p.AssignAsync(TestDaemonId, nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newAssignment);

        var coordinator = CreateCoordinator([mockAgent.Object]);

        // Act
        await coordinator.OnHeartbeatTickAsync(nodeId, isLeader: true, CancellationToken.None, CancellationToken.None);

        // Assert
        mockAssignmentPersistence.Verify(p => p.AssignAsync(TestDaemonId, nodeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenDaemonOnDeadNode_WhenReconcile_ThenReassigns()
    {
        // Arrange
        var deadNodeId = Guid.NewGuid();
        var existingAssignment = MakeAssignment(deadNodeId, DaemonAssignmentState.Active, generation: 1);
        var newAssignment = MakeAssignment(nodeId, DaemonAssignmentState.Assigning, generation: 2);

        mockAssignmentPersistence
            .Setup(p => p.GetAllAssignmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([existingAssignment]);

        mockAssignmentPersistence
            .Setup(p => p.GetNodeAssignmentsAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Only nodeId is live — deadNodeId is not
        mockLeaderElectionPersistence
            .Setup(p => p.GetActiveNodeIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([nodeId]);

        mockAssignmentPersistence
            .Setup(p => p.AssignAsync(TestDaemonId, nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newAssignment);

        var coordinator = CreateCoordinator([mockAgent.Object]);

        // Act
        await coordinator.OnHeartbeatTickAsync(nodeId, isLeader: true, CancellationToken.None, CancellationToken.None);

        // Assert
        mockAssignmentPersistence.Verify(p => p.AssignAsync(TestDaemonId, nodeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenDaemonStuckInAssigning_WhenPastTimeout_ThenReassigns()
    {
        // Arrange
        var frozenUtc = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var assignedAt = frozenUtc.UtcDateTime.AddSeconds(-60); // 60s ago, past 30s timeout

        var stuckAssignment = new DaemonAssignment(
            TestDaemonId,
            nodeId,
            Generation: 1,
            DaemonAssignmentState.Assigning,
            AssignedAt: assignedAt,
            DrainDeadline: null);

        var newAssignment = MakeAssignment(nodeId, DaemonAssignmentState.Assigning, generation: 2);

        var mockTimeProvider = new Mock<TimeProvider>();
        mockTimeProvider.Setup(t => t.GetUtcNow()).Returns(frozenUtc);

        mockAssignmentPersistence
            .Setup(p => p.GetAllAssignmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([stuckAssignment]);

        mockAssignmentPersistence
            .Setup(p => p.GetNodeAssignmentsAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        mockLeaderElectionPersistence
            .Setup(p => p.GetActiveNodeIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([nodeId]);

        mockAssignmentPersistence
            .Setup(p => p.AssignAsync(TestDaemonId, nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newAssignment);

        var coordinator = CreateCoordinator([mockAgent.Object], timeProvider: mockTimeProvider.Object);

        // Act
        await coordinator.OnHeartbeatTickAsync(nodeId, isLeader: true, CancellationToken.None, CancellationToken.None);

        // Assert
        mockAssignmentPersistence.Verify(p => p.AssignAsync(TestDaemonId, nodeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenDaemonStuckInRevoking_WhenPastDeadline_ThenReassigns()
    {
        // Arrange
        var frozenUtc = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var drainDeadline = frozenUtc.UtcDateTime.AddSeconds(-5); // deadline was 5s ago

        var revokingAssignment = new DaemonAssignment(
            TestDaemonId,
            nodeId,
            Generation: 2,
            DaemonAssignmentState.Revoking,
            AssignedAt: frozenUtc.UtcDateTime.AddSeconds(-90),
            DrainDeadline: drainDeadline);

        var newAssignment = MakeAssignment(nodeId, DaemonAssignmentState.Assigning, generation: 3);

        var mockTimeProvider = new Mock<TimeProvider>();
        mockTimeProvider.Setup(t => t.GetUtcNow()).Returns(frozenUtc);

        mockAssignmentPersistence
            .Setup(p => p.GetAllAssignmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([revokingAssignment]);

        mockAssignmentPersistence
            .Setup(p => p.GetNodeAssignmentsAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        mockLeaderElectionPersistence
            .Setup(p => p.GetActiveNodeIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([nodeId]);

        mockAssignmentPersistence
            .Setup(p => p.AssignAsync(TestDaemonId, nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newAssignment);

        var coordinator = CreateCoordinator([mockAgent.Object], timeProvider: mockTimeProvider.Object);

        // Act
        await coordinator.OnHeartbeatTickAsync(nodeId, isLeader: true, CancellationToken.None, CancellationToken.None);

        // Assert
        mockAssignmentPersistence.Verify(p => p.AssignAsync(TestDaemonId, nodeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenActiveDaemonOnLiveNode_WhenReconcile_ThenNoAction()
    {
        // Arrange
        var activeAssignment = MakeAssignment(nodeId, DaemonAssignmentState.Active, generation: 1);

        mockAssignmentPersistence
            .Setup(p => p.GetAllAssignmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([activeAssignment]);

        mockAssignmentPersistence
            .Setup(p => p.GetNodeAssignmentsAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([activeAssignment]);

        mockLeaderElectionPersistence
            .Setup(p => p.GetActiveNodeIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([nodeId]);

        var coordinator = CreateCoordinator([mockAgent.Object]);

        // Act
        await coordinator.OnHeartbeatTickAsync(nodeId, isLeader: true, CancellationToken.None, CancellationToken.None);

        // Assert
        mockAssignmentPersistence.Verify(
            p => p.AssignAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // --- Node assignment processing ---

    [Fact]
    public async Task GivenAssigningState_WhenProcessed_ThenAcknowledgesAndStartsDaemon()
    {
        // Arrange
        var assignment = MakeAssignment(nodeId, DaemonAssignmentState.Assigning, generation: 1);

        mockAssignmentPersistence
            .Setup(p => p.GetNodeAssignmentsAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([assignment]);

        mockAssignmentPersistence
            .Setup(p => p.AcknowledgeAsync(TestDaemonId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var coordinator = CreateCoordinator([mockAgent.Object]);

        // Act
        await coordinator.OnHeartbeatTickAsync(nodeId, isLeader: false, CancellationToken.None, CancellationToken.None);

        // Assert
        mockAssignmentPersistence.Verify(p => p.AcknowledgeAsync(TestDaemonId, 1, It.IsAny<CancellationToken>()), Times.Once);
        mockAgent.Verify(a => a.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenActiveState_WhenDaemonNotRunning_ThenRestartsDaemon()
    {
        // Arrange
        var assignment = MakeAssignment(nodeId, DaemonAssignmentState.Active, generation: 1);

        mockAssignmentPersistence
            .Setup(p => p.GetNodeAssignmentsAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([assignment]);

        var coordinator = CreateCoordinator([mockAgent.Object]);

        // Act — daemon is not running, Active state triggers restart
        await coordinator.OnHeartbeatTickAsync(nodeId, isLeader: false, CancellationToken.None, CancellationToken.None);

        // Assert
        mockAgent.Verify(a => a.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenActiveState_WhenDaemonRunning_ThenNoAction()
    {
        // Arrange
        var assigningAssignment = MakeAssignment(nodeId, DaemonAssignmentState.Assigning, generation: 1);
        var activeAssignment = MakeAssignment(nodeId, DaemonAssignmentState.Active, generation: 1);

        mockAssignmentPersistence
            .Setup(p => p.AcknowledgeAsync(TestDaemonId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // First tick: Assigning → start the daemon
        mockAssignmentPersistence
            .SetupSequence(p => p.GetNodeAssignmentsAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([assigningAssignment])
            .ReturnsAsync([activeAssignment]);

        var coordinator = CreateCoordinator([mockAgent.Object]);

        // Act — first tick starts the daemon
        await coordinator.OnHeartbeatTickAsync(nodeId, isLeader: false, CancellationToken.None, CancellationToken.None);

        // Act — second tick: daemon is already running, Active state should be a no-op
        await coordinator.OnHeartbeatTickAsync(nodeId, isLeader: false, CancellationToken.None, CancellationToken.None);

        // Assert — StartAsync called only once (from the Assigning tick, not the Active tick)
        mockAgent.Verify(a => a.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenRevokingState_WhenProcessed_ThenStopsDaemonAndConfirmsDrain()
    {
        // Arrange
        var assigningAssignment = MakeAssignment(nodeId, DaemonAssignmentState.Assigning, generation: 1);
        var revokingAssignment = MakeAssignment(nodeId, DaemonAssignmentState.Revoking, generation: 2);

        mockAssignmentPersistence
            .Setup(p => p.AcknowledgeAsync(TestDaemonId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        mockAssignmentPersistence
            .Setup(p => p.ConfirmDrainAsync(TestDaemonId, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        mockAssignmentPersistence
            .SetupSequence(p => p.GetNodeAssignmentsAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([assigningAssignment])
            .ReturnsAsync([revokingAssignment]);

        var coordinator = CreateCoordinator([mockAgent.Object]);

        // Act — first tick: start the daemon via Assigning
        await coordinator.OnHeartbeatTickAsync(nodeId, isLeader: false, CancellationToken.None, CancellationToken.None);

        // Act — second tick: Revoking → stop and confirm drain
        await coordinator.OnHeartbeatTickAsync(nodeId, isLeader: false, CancellationToken.None, CancellationToken.None);

        // Assert
        mockAgent.Verify(a => a.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockAssignmentPersistence.Verify(p => p.ConfirmDrainAsync(TestDaemonId, 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenRunningDaemon_WhenNoAssignment_ThenStopsOrphanedDaemon()
    {
        // Arrange
        var assigningAssignment = MakeAssignment(nodeId, DaemonAssignmentState.Assigning, generation: 1);

        mockAssignmentPersistence
            .Setup(p => p.AcknowledgeAsync(TestDaemonId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        mockAssignmentPersistence
            .SetupSequence(p => p.GetNodeAssignmentsAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([assigningAssignment])
            .ReturnsAsync([]); // second tick: no assignments for this node

        var coordinator = CreateCoordinator([mockAgent.Object]);

        // Act — first tick: start the daemon
        await coordinator.OnHeartbeatTickAsync(nodeId, isLeader: false, CancellationToken.None, CancellationToken.None);

        // Act — second tick: daemon has no assignment → orphaned, must be stopped
        await coordinator.OnHeartbeatTickAsync(nodeId, isLeader: false, CancellationToken.None, CancellationToken.None);

        // Assert
        mockAgent.Verify(a => a.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- Shutdown ---

    [Fact]
    public async Task GivenRunningDaemons_WhenStopAll_ThenAllStopped()
    {
        // Arrange
        const string secondDaemonId = "second-daemon";
        var mockAgent2 = new Mock<IDaemonAgent>();
        mockAgent2.Setup(a => a.DaemonId).Returns(secondDaemonId);
        mockAgent2.Setup(a => a.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mockAgent2.Setup(a => a.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var assignment1 = MakeAssignment(nodeId, DaemonAssignmentState.Assigning, generation: 1);
        var assignment2 = new DaemonAssignment(
            secondDaemonId, nodeId, 1, DaemonAssignmentState.Assigning,
            AssignedAt: DateTime.UtcNow, DrainDeadline: null);

        mockAssignmentPersistence
            .Setup(p => p.AcknowledgeAsync(TestDaemonId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        mockAssignmentPersistence
            .Setup(p => p.AcknowledgeAsync(secondDaemonId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        mockAssignmentPersistence
            .Setup(p => p.GetNodeAssignmentsAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([assignment1, assignment2]);

        var coordinator = CreateCoordinator([mockAgent.Object, mockAgent2.Object]);

        // Act — tick to start both daemons
        await coordinator.OnHeartbeatTickAsync(nodeId, isLeader: false, CancellationToken.None, CancellationToken.None);

        // Act — shut down
        await coordinator.StopAllDaemonsAsync(CancellationToken.None);

        // Assert
        mockAgent.Verify(a => a.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockAgent2.Verify(a => a.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- No daemons ---

    [Fact]
    public async Task GivenNoDaemons_WhenHeartbeatTick_ThenReturnsImmediately()
    {
        // Arrange
        var coordinator = CreateCoordinator([]);

        // Act
        await coordinator.OnHeartbeatTickAsync(nodeId, isLeader: true, CancellationToken.None, CancellationToken.None);

        // Assert — nothing called since there are no daemons
        mockAssignmentPersistence.Verify(
            p => p.GetAllAssignmentsAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        mockAssignmentPersistence.Verify(
            p => p.GetNodeAssignmentsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // --- Helpers ---

    private DaemonAssignment MakeAssignment(
        Guid assignedNodeId,
        DaemonAssignmentState state,
        long generation = 1) =>
        new(
            TestDaemonId,
            assignedNodeId,
            generation,
            state,
            AssignedAt: DateTime.UtcNow,
            DrainDeadline: null);

    private DaemonCoordinator CreateCoordinator(
        IEnumerable<IDaemonAgent> agents,
        DaemonOptions? daemonOptions = null,
        TimeProvider? timeProvider = null) =>
        new(
            mockAssignmentPersistence.Object,
            mockLeaderElectionPersistence.Object,
            agents,
            new DaemonObserverInvoker([], NullLogger<DaemonObserverInvoker>.Instance),
            Options.Create(daemonOptions ?? new DaemonOptions()),
            timeProvider ?? TimeProvider.System,
            NullLogger<DaemonCoordinator>.Instance);
}

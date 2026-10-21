namespace Emit.UnitTests.Daemon;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Daemon;
using global::Emit.Abstractions.LeaderElection;
using global::Emit.Abstractions.Observability;
using global::Emit.Configuration;
using global::Emit.Daemon;
using global::Emit.LeaderElection;
using global::Emit.Observability;
using global::Emit.UnitTests.Coordination;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

public class DaemonCoordinatorTests
{
    private const string ServiceName = "test-service";
    private const string TestDaemonId = "test-daemon";

    private static readonly CancellationToken NotLeader = new(canceled: true);

    private readonly Mock<IDaemonAssignmentPersistence> mockAssignmentPersistence = new();
    private readonly Mock<ILeaderElectionPersistence> mockLeaderElectionPersistence = new();
    private readonly Mock<IDaemonObserver> mockObserver = new();
    private readonly Mock<ILeaderElectionObserver> mockLeaderElectionObserver = new();
    private readonly List<NodeRegistration> silentNodes = [];
    private readonly LeaseTokenSource heldLease = Leases.Held();
    private readonly ControllableDaemonAgent agent = new(TestDaemonId);
    private readonly Guid nodeId = Guid.NewGuid();
    private readonly TaskCompletionSource hangReached = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // --- Reconciliation (leader=true) ---

    [Theory]
    [InlineData(DaemonAssignmentState.Active, false, true, Outcome.Moved)]
    [InlineData(DaemonAssignmentState.Revoking, false, true, Outcome.Moved)]
    [InlineData(DaemonAssignmentState.Active, false, false, Outcome.Moved)]
    [InlineData(DaemonAssignmentState.Assigning, true, true, Outcome.Moved)]
    [InlineData(DaemonAssignmentState.Assigning, true, false, Outcome.Revoked)]
    [InlineData(DaemonAssignmentState.Active, true, true, Outcome.Revoked)]
    [InlineData(DaemonAssignmentState.Active, true, false, Outcome.Revoked)]
    [InlineData(DaemonAssignmentState.Revoking, true, true, Outcome.Kept)]
    public async Task GivenDaemonOnANodeThatIsGoneOrNoLongerDeclaresIt_WhenReconcile_ThenItIsMovedAtOnceOnlyIfTheNodeCannotBeRunningIt(
        DaemonAssignmentState state,
        bool nodeStillRegistered,
        bool anotherNodeDeclaresIt,
        Outcome outcome)
    {
        // Arrange: a node that is gone fenced its daemons before its registration expired, and one that has not
        // acknowledged has not started it, which a reassignment checks; any other node that is alive is asked to stop
        // it first. A daemon no node declares anymore is removed rather than reassigned when its node is gone.
        var assignedNodeId = Guid.NewGuid();
        var otherNodeId = Guid.NewGuid();
        var current = MakeAssignment(assignedNodeId, state, generation: 2);
        List<NodeRegistration> nodes = [Node(otherNodeId, anotherNodeDeclaresIt ? [TestDaemonId] : [])];
        if (nodeStillRegistered)
        {
            nodes.Add(Node(assignedNodeId, []));
        }

        SetupNodes([.. nodes]);
        SetupAssignments(current);
        mockAssignmentPersistence
            .Setup(p => p.RemoveAsync(ServiceName, TestDaemonId, current.Generation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var coordinator = CreateCoordinator([]);

        // Act
        await LeaderTickAsync(coordinator);

        // Assert
        var moved = outcome == Outcome.Moved;
        mockAssignmentPersistence.Verify(
            p => p.AssignAsync(ServiceName, TestDaemonId, otherNodeId, current, It.IsAny<CancellationToken>()),
            moved && anotherNodeDeclaresIt ? Times.Once() : Times.Never());
        mockAssignmentPersistence.Verify(
            p => p.RemoveAsync(ServiceName, TestDaemonId, current.Generation, It.IsAny<CancellationToken>()),
            moved && !anotherNodeDeclaresIt ? Times.Once() : Times.Never());
        mockAssignmentPersistence.Verify(
            p => p.RevokeAsync(ServiceName, TestDaemonId, current.Generation, It.IsAny<CancellationToken>()),
            outcome == Outcome.Revoked ? Times.Once() : Times.Never());
    }

    // --- Node assignment processing ---

    [Fact]
    public async Task GivenAssigningAssignment_WhenProcessedAndThenActive_ThenAcknowledgesAndStartsTheDaemonOnceWithItsGenerationAndFencingKey()
    {
        // Arrange: the run's generation and fencing key let the daemon fence the runs before it
        var assigning = MakeAssignment(nodeId, DaemonAssignmentState.Assigning, generation: 1) with { FencingKey = Guid.NewGuid() };
        SetupNodeAssignments(assigning);
        var coordinator = CreateCoordinator([agent]);

        // Act
        await FollowerTickAsync(coordinator);
        SetupNodeAssignments(assigning with { State = DaemonAssignmentState.Active });
        await FollowerTickAsync(coordinator);

        // Assert
        mockAssignmentPersistence.Verify(p => p.AcknowledgeAsync(ServiceName, TestDaemonId, 1, It.IsAny<CancellationToken>()), Times.Once);
        mockObserver.Verify(o => o.OnDaemonStartedAsync(TestDaemonId, nodeId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, agent.RunCount);
        Assert.True(agent.IsRunning);
        Assert.Equal((assigning.Generation, assigning.FencingKey), (agent.LastContext!.Generation, agent.LastContext.FencingKey));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenRevokedDaemon_WhenProcessed_ThenItIsStoppedAndItsDrainConfirmedOnlyOnceItsRunEnds(bool runIgnoresCancellation)
    {
        // Arrange: confirming the drain lets the leader start the daemon elsewhere. The heartbeat does not wait
        // for a run that keeps going.
        agent.IgnoresCancellation = runIgnoresCancellation;
        SetupNodeAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Active, generation: 1));
        mockAssignmentPersistence
            .Setup(p => p.ConfirmDrainAsync(ServiceName, TestDaemonId, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var coordinator = CreateCoordinator([agent]);
        await FollowerTickAsync(coordinator);
        SetupNodeAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Revoking, generation: 2));

        // Act
        await FollowerTickAsync(coordinator).WaitAsync(TimeSpan.FromSeconds(30));
        if (runIgnoresCancellation)
        {
            await FollowerTickAsync(coordinator).WaitAsync(TimeSpan.FromSeconds(30));

            // Assert
            mockAssignmentPersistence.Verify(
                p => p.ConfirmDrainAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
                Times.Never);

            // Act
            agent.Return();
            await FollowerTickAsync(coordinator).WaitAsync(TimeSpan.FromSeconds(30));
        }

        // Assert
        Assert.False(agent.IsRunning);
        mockAssignmentPersistence.Verify(p => p.ConfirmDrainAsync(ServiceName, TestDaemonId, 2, It.IsAny<CancellationToken>()), Times.Once);
        mockObserver.Verify(o => o.OnDaemonStoppedAsync(TestDaemonId, nodeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- Timeouts measured by the leader ---

    [Fact]
    public async Task GivenUnacknowledgedAssignment_WhenItIsReassignedMeanwhile_ThenTheAcknowledgeTimeoutRestartsWithTheNewGenerationAndOnlyThenItIsReassignedToAnotherNode()
    {
        // Arrange: the timeout is measured by the leader from when it first saw the assignment's generation, never
        // from a time the database records, so a reassignment meanwhile starts it over. The node that did not
        // acknowledge is the least loaded, but giving the daemon back to it could repeat forever.
        var time = NewTime();
        var daemonOptions = new DaemonOptions();
        var other = Guid.NewGuid();
        SetupNodes(Node(nodeId, [TestDaemonId]), Node(other, [TestDaemonId, "o1", "o2"]));
        SetupAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Assigning, generation: 1), ActiveOn(other, "o1"), ActiveOn(other, "o2"));
        var coordinator = CreateCoordinator([agent], daemonOptions, time);
        await LeaderTickAsync(coordinator);
        time.Advance(daemonOptions.AcknowledgeTimeout - TimeSpan.FromMilliseconds(1));
        var reassigned = MakeAssignment(nodeId, DaemonAssignmentState.Assigning, generation: 2);
        SetupAssignments(reassigned, ActiveOn(other, "o1"), ActiveOn(other, "o2"));
        await LeaderTickAsync(coordinator);

        // Act
        time.Advance(TimeSpan.FromMilliseconds(2));
        await LeaderTickAsync(coordinator);

        // Assert
        VerifyNoAssignment();

        // Act
        time.Advance(daemonOptions.AcknowledgeTimeout - TimeSpan.FromMilliseconds(2));
        await LeaderTickAsync(coordinator);

        // Assert
        VerifyNoAssignment();

        // Act
        time.Advance(TimeSpan.FromMilliseconds(1));
        await LeaderTickAsync(coordinator);

        // Assert
        mockAssignmentPersistence.Verify(
            p => p.AssignAsync(ServiceName, TestDaemonId, other, reassigned, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GivenRevokingAssignmentOnALiveNode_WhenItIsNotConfirmedForTheNodesRegistrationTtl_ThenItIsReassignedThenAndNotBefore()
    {
        // Arrange: the node's own options keep its registration TTL longer than its lease, drain timeout and a
        // heartbeat: by then a node holding its lease has read its assignments since the revocation and stopped
        // the daemon, one that does not lost its lease, and the cancelled run has ended. The node's timings can
        // be longer than the leader's, as during a rolling deployment that changes them.
        var time = NewTime();
        var registrationTtl = new LeaderElectionOptions().NodeRegistrationTtl * 3;
        var other = Guid.NewGuid();
        SetupNodes(Node(nodeId, [TestDaemonId], registrationTtl: registrationTtl), Node(other, [TestDaemonId]));
        var revoking = MakeAssignment(nodeId, DaemonAssignmentState.Revoking, generation: 2);
        SetupAssignments(revoking);
        var coordinator = CreateCoordinator([agent], timeProvider: time);
        await LeaderTickAsync(coordinator);

        // Act
        time.Advance(registrationTtl - TimeSpan.FromMilliseconds(1));
        await LeaderTickAsync(coordinator);

        // Assert
        VerifyNoAssignment();

        // Act
        time.Advance(TimeSpan.FromMilliseconds(1));
        await LeaderTickAsync(coordinator);

        // Assert
        mockAssignmentPersistence.Verify(
            p => p.AssignAsync(ServiceName, TestDaemonId, other, revoking, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // --- Placement ---

    [Theory]
    [InlineData(Placement.OnlyOnADeclaringNode)]
    [InlineData(Placement.OnTheLeastLoadedNode)]
    [InlineData(Placement.OnAResponsiveNodeBeforeALessLoadedSilentOne)]
    [InlineData(Placement.OnASilentNodeWhenNoneIsResponsive)]
    public async Task GivenUnassignedDaemon_WhenReconcile_ThenAssignsItIfStillUnassignedToTheNodeThePlacementRulesPick(Placement placement)
    {
        // Arrange
        var expected = Guid.NewGuid();
        var other = Guid.NewGuid();
        switch (placement)
        {
            case Placement.OnlyOnADeclaringNode:
                SetupNodes(Node(other, []), Node(expected, [TestDaemonId]));
                SetupAssignments();
                break;
            case Placement.OnTheLeastLoadedNode:
                SetupNodes(Node(other, [TestDaemonId, "other"]), Node(expected, [TestDaemonId]));
                SetupAssignments(ActiveOn(other, "other"));
                break;
            case Placement.OnAResponsiveNodeBeforeALessLoadedSilentOne:
                SetupNodes(
                    Node(other, [TestDaemonId], renews: false),
                    Node(expected, [TestDaemonId, "other"]));
                SetupAssignments(ActiveOn(expected, "other"));
                break;
            case Placement.OnASilentNodeWhenNoneIsResponsive:
                SetupNodes(Node(expected, [TestDaemonId], renews: false));
                SetupAssignments();
                break;
        }

        var coordinator = CreateCoordinator([]);
        await FollowerTickAsync(coordinator);

        // Act
        await LeaderTickAsync(coordinator);

        // Assert
        mockAssignmentPersistence.Verify(
            p => p.AssignAsync(ServiceName, TestDaemonId, expected, null, It.IsAny<CancellationToken>()),
            Times.Once);
        mockAssignmentPersistence.Verify(
            p => p.AssignAsync(ServiceName, TestDaemonId, It.Is<Guid>(id => id != expected), It.IsAny<DaemonAssignment?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        mockObserver.Verify(o => o.OnDaemonAssignedAsync(TestDaemonId, expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- Daemons that fail ---

    [Fact]
    public async Task GivenDaemonStoppedAfterRepeatedFailures_WhenTheFailureCooldownElapses_ThenTheNodeRunsItOnlyOnceAHeartbeatDeclaredItAgain()
    {
        // Arrange: after the last allowed failure the node neither runs nor declares the daemon, however many
        // heartbeats follow, while another node offers it
        var time = NewTime();
        var daemonOptions = new DaemonOptions { MaxConsecutiveFailures = 3 };
        agent.FailNextRuns = 3;
        OfferedByAnotherNode(TestDaemonId);
        SetupNodeAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Active));
        var coordinator = CreateCoordinator([agent], daemonOptions, time);
        for (var i = 0; i < 6; i++)
        {
            await FollowerTickAsync(coordinator);
        }

        Assert.Equal(3, agent.RunCount);
        var declared = DeclareDaemons(coordinator);
        Assert.DoesNotContain(TestDaemonId, declared);

        // Act: the cooldown ends between the heartbeat that left the daemon out and the work that follows it;
        // the leader may be moving the daemon on the strength of that heartbeat
        time.Advance(daemonOptions.FailureCooldown);
        await FollowerTickAsync(coordinator);

        // Assert
        Assert.Equal(3, agent.RunCount);

        // Act
        var declaredAgain = DeclareDaemons(coordinator);
        await FollowerTickAsync(coordinator);

        // Assert
        Assert.Contains(TestDaemonId, declaredAgain);
        Assert.True(agent.IsRunning);
    }

    [Fact]
    public async Task GivenDaemonThatKeepsFailing_WhenNoOtherNodeOffersIt_ThenTheNodeKeepsOfferingAndRetryingIt()
    {
        // Arrange: giving a failing daemon up only helps when another node can run it; with none, it would only stop
        // the daemon, for example the outbox for the whole cooldown after a short broker outage
        var daemonOptions = new DaemonOptions { MaxConsecutiveFailures = 3 };
        agent.FailNextRuns = 3;
        SetupNodes(Node(nodeId, [TestDaemonId]));
        SetupNodeAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Active));
        var coordinator = CreateCoordinator([agent], daemonOptions, NewTime());

        // Act
        for (var i = 0; i < daemonOptions.MaxConsecutiveFailures + 1; i++)
        {
            await FollowerTickAsync(coordinator);
        }

        // Assert
        Assert.Contains(TestDaemonId, DeclareDaemons(coordinator));
        Assert.Equal(daemonOptions.MaxConsecutiveFailures + 1, agent.RunCount);
        Assert.True(agent.IsRunning);
    }

    [Fact]
    public async Task GivenCancelledRunThatDoesNotEnd_WhenTheDrainTimeoutPasses_ThenTheNodeOffersTheDaemonOnlyOnceTheRunEnds()
    {
        // Arrange: every run is expected to end within the drain timeout of being cancelled. A node that cannot run
        // the daemon again before its run ends stops offering it, so the leader moves it instead of waiting forever.
        var time = NewTime();
        var daemonOptions = new DaemonOptions();
        var stuck = new ControllableDaemonAgent(TestDaemonId) { IgnoresCancellation = true };
        SetupNodeAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Active));
        var coordinator = CreateCoordinator([stuck], daemonOptions, time);
        await FollowerTickAsync(coordinator);
        SetupNodeAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Revoking, generation: 2));
        await FollowerTickAsync(coordinator);

        // Act
        time.Advance(daemonOptions.DrainTimeout - TimeSpan.FromMilliseconds(1));
        var declared = DeclareDaemons(coordinator);

        // Assert
        Assert.Contains(TestDaemonId, declared);

        // Act
        time.Advance(TimeSpan.FromMilliseconds(1));
        declared = DeclareDaemons(coordinator);

        // Assert
        Assert.DoesNotContain(TestDaemonId, declared);

        // Act
        stuck.Return();
        declared = DeclareDaemons(coordinator);

        // Assert
        Assert.Contains(TestDaemonId, declared);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task GivenDaemonThatKeepsEndingUnasked_WhenEachRunLastedTheFailureCooldownOrNot_ThenItIsRestartedAndTheNodeKeepsOfferingItOnlyIfItDid(
        bool runsLastTheCooldown,
        bool returns)
    {
        // Arrange: a run that ends without being asked to, by throwing or returning, failed. A failure counts toward
        // the limit only after a run shorter than the failure cooldown, so a daemon that keeps failing after a while
        // is caught as surely as one that fails at once. Another node offers the daemon, so it can move there.
        var time = NewTime();
        var daemonOptions = new DaemonOptions();
        OfferedByAnotherNode(TestDaemonId);
        SetupNodeAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Active));
        var coordinator = CreateCoordinator([agent], daemonOptions, time);
        var runLength = runsLastTheCooldown ? daemonOptions.FailureCooldown : daemonOptions.FailureCooldown - TimeSpan.FromTicks(1);

        // Act
        for (var i = 0; i < daemonOptions.MaxConsecutiveFailures; i++)
        {
            await FollowerTickAsync(coordinator);
            time.Advance(runLength);
            if (returns)
            {
                agent.Return();
            }
            else
            {
                agent.Crash();
            }
        }

        await FollowerTickAsync(coordinator);
        var declared = DeclareDaemons(coordinator);

        // Assert
        Assert.Equal(runsLastTheCooldown, declared.Contains(TestDaemonId));
        Assert.Equal(runsLastTheCooldown, agent.IsRunning);
    }

    [Fact]
    public async Task GivenDaemonThatReturnsOnceItNoLongerHoldsItsAssignment_WhenItsLeaseRanOutBeforeItsTokenWasCancelled_ThenTheEndIsNotCountedAsAFailure()
    {
        // Arrange: a run that stops because it no longer holds its assignment did what it should, even when the
        // lease's token is cancelled late, as after a suspend or on a busy thread pool; a single failure would put
        // the daemon into its cooldown here
        var clock = new SteppingClock(DateTimeOffset.UtcNow);
        var coordinator = CreateCoordinator([agent], new DaemonOptions { MaxConsecutiveFailures = 1 }, clock);
        OfferedByAnotherNode(TestDaemonId);
        SetupNodeAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Active));
        var busyCallbacks = new QueuedTaskScheduler();
        using var lease = new LeaseTokenSource(clock, busyCallbacks, TimeSpan.FromSeconds(1), NullLogger.Instance);
        lease.ExpireAt(ClockReading.Now(clock), TimeSpan.FromMinutes(1));
        await coordinator.OnHeartbeatTickAsync(nodeId, NotLeader, () => lease, CancellationToken.None);
        clock.StepWallClock(TimeSpan.FromMinutes(1));
        Assert.False(agent.LastContext!.HoldsAssignment);
        agent.Return();

        // Act
        await FollowerTickAsync(coordinator);

        // Assert
        Assert.Contains(TestDaemonId, DeclareDaemons(coordinator));
    }

    // --- Generations on the node ---

    [Theory]
    [InlineData(RunEnd.LeaseRunsOut, false)]
    [InlineData(RunEnd.NewGenerationAssigned, false)]
    [InlineData(RunEnd.NewGenerationActive, false)]
    [InlineData(RunEnd.LeaseRunsOut, true)]
    [InlineData(RunEnd.NewGenerationAssigned, true)]
    [InlineData(RunEnd.NewGenerationActive, true)]
    public async Task GivenRunningDaemon_WhenItsRunIsStopped_ThenTheNextRunStartsOnlyOnceTheStoppedOneEnded(
        RunEnd stop,
        bool runIgnoresCancellation)
    {
        // Arrange
        agent.IgnoresCancellation = runIgnoresCancellation;
        SetupNodeAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Active, generation: 1));
        var coordinator = CreateCoordinator([agent]);
        using var firstLease = Leases.Held();
        await coordinator.OnHeartbeatTickAsync(nodeId, NotLeader, () => firstLease, CancellationToken.None);
        var firstRunToken = agent.LastToken;

        // Act
        switch (stop)
        {
            case RunEnd.LeaseRunsOut:
                firstLease.Cancel();
                break;
            case RunEnd.NewGenerationAssigned:
                SetupNodeAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Assigning, generation: 2));
                break;
            default:
                SetupNodeAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Active, generation: 2));
                break;
        }

        await FollowerTickAsync(coordinator).WaitAsync(TimeSpan.FromSeconds(30));
        if (runIgnoresCancellation)
        {
            // Assert
            Assert.Equal(1, agent.RunCount);

            // Act
            agent.Return();
            await FollowerTickAsync(coordinator).WaitAsync(TimeSpan.FromSeconds(30));
        }

        // Assert
        Assert.True(firstRunToken.IsCancellationRequested);
        Assert.Equal(2, agent.RunCount);
        Assert.Equal(1, agent.MaxConcurrentRuns);
        Assert.True(agent.IsRunning);
    }

    // --- Daemons that do not stop ---

    [Fact]
    public async Task GivenDaemonIgnoringCancellation_WhenShuttingDown_ThenWaitsForItAtMostTheDrainTimeout()
    {
        // Arrange
        agent.IgnoresCancellation = true;
        var time = NewTime();
        var daemonOptions = new DaemonOptions();
        SetupNodeAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Active));
        var coordinator = CreateCoordinator([agent], daemonOptions, time);
        await FollowerTickAsync(coordinator);
        var stopping = coordinator.StopAllDaemonsAsync();

        // Act
        time.Advance(daemonOptions.DrainTimeout - TimeSpan.FromMilliseconds(1));

        // Assert
        Assert.False(stopping.IsCompleted);

        // Act
        time.Advance(TimeSpan.FromMilliseconds(1));

        // Assert
        await stopping.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task GivenRunThatDoesNotStop_WhenTheNodeReleasesItsAssignments_ThenItKeepsThatAssignmentUntilTheRunEndsAndThenReleasesItInsteadOfRestarting()
    {
        // Arrange: releasing at once would let the leader start the daemon elsewhere while it still runs here,
        // and restarting it under the kept generation would let a decision the leader took while this node
        // looked gone move it while it runs
        agent.IgnoresCancellation = true;
        SetupNodeAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Active, generation: 1));
        var coordinator = CreateCoordinator([agent]);
        await FollowerTickAsync(coordinator);

        // Act
        await coordinator.ReleaseAssignmentsAsync(nodeId, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
        await FollowerTickAsync(coordinator);

        // Assert
        mockAssignmentPersistence.Verify(
            p => p.RemoveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Act
        mockAssignmentPersistence
            .Setup(p => p.RemoveAsync(ServiceName, TestDaemonId, 1, It.IsAny<CancellationToken>()))
            .Callback(() => SetupNodeAssignments())
            .ReturnsAsync(true);
        agent.Return();
        await FollowerTickAsync(coordinator);
        await FollowerTickAsync(coordinator);

        // Assert
        Assert.Equal(1, agent.RunCount);
        mockAssignmentPersistence.Verify(p => p.RemoveAsync(ServiceName, TestDaemonId, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(DaemonBlocks.WhenStarted)]
    [InlineData(DaemonBlocks.WhenStoppedByTheNode)]
    [InlineData(DaemonBlocks.WhenFenced)]
    public async Task GivenDaemonThatBlocksTheThreadThatStartsOrStopsIt_WhenThatHappens_ThenNeitherTheHeartbeatNorAnotherDaemonWaitsForIt(
        DaemonBlocks blocks)
    {
        // Arrange: cancelling a token runs the daemon's callbacks and continuations on the cancelling thread,
        // for example a Kafka producer flushed synchronously once the daemon is stopped
        using var release = new ManualResetEventSlim();
        using var lease = Leases.Held();
        var blocking = new BlockingDaemonAgent("blocking", release, blocksWhileStopping: blocks != DaemonBlocks.WhenStarted);
        SetupNodeAssignments(
            new DaemonAssignment("blocking", nodeId, 1, DaemonAssignmentState.Active, DateTime.UtcNow),
            MakeAssignment(nodeId, DaemonAssignmentState.Active));
        var coordinator = CreateCoordinator([blocking, agent], runScheduler: TaskScheduler.Default);
        Task Tick() => Task.Run(() => coordinator.OnHeartbeatTickAsync(nodeId, NotLeader, () => lease, CancellationToken.None));

        try
        {
            var start = Tick();
            if (blocks != DaemonBlocks.WhenStarted)
            {
                await start.WaitAsync(TimeSpan.FromSeconds(30));
                await blocking.Started.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.True(SpinWait.SpinUntil(() => agent.IsRunning, TimeSpan.FromSeconds(30)));
            }

            // Act
            var heartbeat = blocks switch
            {
                DaemonBlocks.WhenStarted => start,
                DaemonBlocks.WhenStoppedByTheNode => StopBothAndTick(),
                _ => Task.Run(lease.Cancel),
            };

            // Assert
            await heartbeat.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(SpinWait.SpinUntil(
                () => blocks == DaemonBlocks.WhenStarted ? agent.IsRunning : !agent.IsRunning,
                TimeSpan.FromSeconds(30)));
        }
        finally
        {
            release.Set();
        }

        Task StopBothAndTick()
        {
            SetupNodeAssignments();
            return Tick();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenDatabaseCallThatNeverReturns_WhenProcessingAssignments_ThenTheHeartbeatGivesUpExactlyAfterTheQueryTimeout(
        bool clientHonoursCancellation)
    {
        // Arrange: a client that does not honour cancellation must not hang the node either
        var time = NewTime();
        var leaderElectionOptions = new LeaderElectionOptions();
        mockAssignmentPersistence
            .Setup(p => p.GetNodeAssignmentsAsync(ServiceName, nodeId, It.IsAny<CancellationToken>()))
            .Returns((string _, Guid _, CancellationToken token) => clientHonoursCancellation
                ? NeverAsync<IReadOnlyList<DaemonAssignment>>(token)
                : new TaskCompletionSource<IReadOnlyList<DaemonAssignment>>().Task);
        var coordinator = CreateCoordinator([agent], timeProvider: time);

        // Act
        var tick = FollowerTickAsync(coordinator);
        time.Advance(leaderElectionOptions.QueryTimeout - TimeSpan.FromMilliseconds(1));

        // Assert
        Assert.False(tick.IsCompleted);

        // Act
        time.Advance(TimeSpan.FromMilliseconds(1));

        // Assert
        await tick.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenRunningDaemon_WhenItIsStoppedOrItsLeaseRunsOut_ThenItNoLongerHoldsItsAssignmentBeforeItsTokenIsCancelled(bool stopped)
    {
        // Arrange: tokens are cancelled on schedulers that can be slow to get to them, as a busy thread pool is, and a
        // lease can run out before its timers run, as after a suspend; a new lease must not revive the run either
        var clock = new SteppingClock(DateTimeOffset.UtcNow);
        var runScheduler = new QueuedTaskScheduler();
        SetupNodeAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Active));
        var coordinator = CreateCoordinator([agent], runScheduler: runScheduler);
        using var lease = Leases.ExpiringAfter(clock, TimeSpan.FromMinutes(1));
        await coordinator.OnHeartbeatTickAsync(nodeId, NotLeader, () => lease, CancellationToken.None);
        runScheduler.RunQueued();
        var run = agent.LastContext!;
        Assert.True(run.HoldsAssignment);

        // Act
        if (stopped)
        {
            SetupNodeAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Revoking, generation: 2));
            await coordinator.OnHeartbeatTickAsync(nodeId, NotLeader, () => heldLease, CancellationToken.None);
        }
        else
        {
            clock.StepWallClock(TimeSpan.FromMinutes(1));
        }

        // Assert
        Assert.False(run.AssignmentToken.IsCancellationRequested);
        Assert.False(run.HoldsAssignment);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenFencedNode_WhenAnActiveAssignmentIsProcessed_ThenTheDaemonIsNotStarted(bool leaseRanOutBeforeTheTimers)
    {
        // Arrange: after a pause or a suspend, the lease can have run out before the timers that cancel the worker
        // token have run
        var clock = new SteppingClock(DateTimeOffset.UtcNow);
        SetupNodeAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Active));
        var coordinator = CreateCoordinator([agent]);
        using var lease = leaseRanOutBeforeTheTimers ? Leases.LostInASuspend(clock) : Leases.Held();
        if (!leaseRanOutBeforeTheTimers)
        {
            lease.Cancel();
        }

        // Act
        await coordinator.OnHeartbeatTickAsync(nodeId, NotLeader, () => lease, CancellationToken.None);

        // Assert
        Assert.Equal(0, agent.RunCount);
    }

    [Fact]
    public async Task GivenOneDaemonWhoseAssignmentFails_WhenReconciling_ThenTheOtherDaemonsAreStillAssigned()
    {
        // Arrange
        SetupNodes(Node(nodeId, ["a", "b"]));
        SetupAssignments();
        mockAssignmentPersistence
            .Setup(p => p.AssignAsync(ServiceName, "a", It.IsAny<Guid>(), It.IsAny<DaemonAssignment?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));
        var coordinator = CreateCoordinator([]);

        // Act
        await LeaderTickAsync(coordinator);

        // Assert
        mockAssignmentPersistence.Verify(
            p => p.AssignAsync(ServiceName, "b", nodeId, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(InvalidDaemons.EmptyId)]
    [InlineData(InvalidDaemons.BlankId)]
    [InlineData(InvalidDaemons.IdLongerThanTheLimit)]
    [InlineData(InvalidDaemons.TwoWithTheSameId)]
    public void GivenDaemonsWithoutUsableUniqueIds_WhenCreatingTheCoordinator_ThenThrows(InvalidDaemons daemons)
    {
        // Arrange
        string[] ids = daemons switch
        {
            InvalidDaemons.EmptyId => [""],
            InvalidDaemons.BlankId => ["   "],
            InvalidDaemons.IdLongerThanTheLimit => [new string('d', ValidationConstants.MaxDaemonIdLength + 1)],
            _ => ["same", "same"],
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => CreateCoordinator(ids.Select(id => new ControllableDaemonAgent(id))));
    }

    [Fact]
    public async Task GivenOneAssignmentWhoseHandlingFails_WhenTheNodeProcessesItsAssignments_ThenTheOthersAreStillHandled()
    {
        // Arrange: a persistent error on one assignment's row must not hold up the node's other daemons
        var broken = new ControllableDaemonAgent("broken");
        var orphan = new ControllableDaemonAgent("orphan");
        SetupNodeAssignments(new DaemonAssignment("orphan", nodeId, 1, DaemonAssignmentState.Active, DateTime.UtcNow));
        var coordinator = CreateCoordinator([agent, broken, orphan]);
        await FollowerTickAsync(coordinator);
        Assert.True(orphan.IsRunning);
        SetupNodeAssignments(
            new DaemonAssignment("broken", nodeId, 1, DaemonAssignmentState.Assigning, DateTime.UtcNow),
            MakeAssignment(nodeId, DaemonAssignmentState.Active));
        mockAssignmentPersistence
            .Setup(p => p.AcknowledgeAsync(ServiceName, "broken", It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act
        await FollowerTickAsync(coordinator);

        // Assert
        Assert.True(agent.IsRunning);
        Assert.False(orphan.IsRunning);
        Assert.False(broken.IsRunning);
    }

    [Fact]
    public async Task GivenNodeGivingUpSeveralAssignments_WhenOneReleaseNeverReturns_ThenTheOthersAreStillReleased()
    {
        // Arrange: a node shutting down gives up its daemons within the host's shutdown timeout, however many
        var other = new ControllableDaemonAgent("other");
        var coordinator = CreateCoordinator([other, agent]);
        mockAssignmentPersistence
            .Setup(p => p.GetNodeAssignmentsAsync(ServiceName, nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeAssignment(nodeId, DaemonAssignmentState.Active, daemonId: "other"), MakeAssignment(nodeId, DaemonAssignmentState.Active)]);
        mockAssignmentPersistence
            .Setup(p => p.RemoveAsync(ServiceName, "other", It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string _, long _, CancellationToken token) => NeverAsync<bool>(token));
        using var outOfTime = new CancellationTokenSource();

        // Act
        var release = coordinator.ReleaseAssignmentsAsync(nodeId, outOfTime.Token);
        await hangReached.Task.WaitAsync(TimeSpan.FromSeconds(30));
        outOfTime.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => release.WaitAsync(TimeSpan.FromSeconds(30)));

        // Assert
        mockAssignmentPersistence.Verify(p => p.RemoveAsync(ServiceName, TestDaemonId, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(DaemonAssignmentState.Assigning)]
    [InlineData(DaemonAssignmentState.Revoking)]
    public async Task GivenAssignmentToAcknowledge_WhenAnotherDaemonsWriteNeverReturns_ThenItIsStillAcknowledged(DaemonAssignmentState otherState)
    {
        // Arrange: the leader moves a daemon its node does not acknowledge in time, and neither another
        // acknowledgement nor a drain confirmation, read first, may hold it up
        var other = new ControllableDaemonAgent("other");
        var coordinator = CreateCoordinator([other, agent]);
        mockAssignmentPersistence
            .Setup(p => p.GetNodeAssignmentsAsync(ServiceName, nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeAssignment(nodeId, otherState, daemonId: "other"), MakeAssignment(nodeId, DaemonAssignmentState.Assigning)]);
        mockAssignmentPersistence
            .Setup(p => p.AcknowledgeAsync(ServiceName, "other", It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string _, long _, CancellationToken token) => NeverAsync<bool>(token));
        mockAssignmentPersistence
            .Setup(p => p.ConfirmDrainAsync(ServiceName, "other", It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string _, long _, CancellationToken token) => NeverAsync<bool>(token));
        mockAssignmentPersistence
            .Setup(p => p.AcknowledgeAsync(ServiceName, TestDaemonId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        using var outOfTime = new CancellationTokenSource();

        // Act
        var tick = coordinator.OnHeartbeatTickAsync(nodeId, NotLeader, () => heldLease, outOfTime.Token);
        await hangReached.Task.WaitAsync(TimeSpan.FromSeconds(30));
        outOfTime.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tick.WaitAsync(TimeSpan.FromSeconds(30)));

        // Assert
        mockAssignmentPersistence.Verify(
            p => p.AcknowledgeAsync(ServiceName, TestDaemonId, 1, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GivenDaemonWhoseIdentifierChangesAfterTheNodeStarted_WhenTheNodeHeartbeats_ThenItIsStillDeclaredAndRunUnderItsFirstIdentifier()
    {
        // Arrange: runs are tracked by identifier, so one that changed would let the node run the daemon twice
        var renamed = new Mock<IDaemonAgent>();
        renamed.SetupSequence(d => d.DaemonId).Returns(TestDaemonId).Returns("renamed");
        renamed.Setup(d => d.RunAsync(It.IsAny<DaemonRunContext>())).Returns(new TaskCompletionSource().Task);
        var coordinator = CreateCoordinator([renamed.Object]);
        SetupNodeAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Active));

        // Act
        var declared = DeclareDaemons(coordinator);
        await FollowerTickAsync(coordinator);
        await FollowerTickAsync(coordinator);

        // Assert
        Assert.Equal([TestDaemonId], declared);
        renamed.Verify(d => d.RunAsync(It.IsAny<DaemonRunContext>()), Times.Once);
    }


    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenRunTheNodeMustNoLongerKeep_WhenAWriteBeforeItsTurnUsesUpTheTick_ThenItIsStoppedBeforeTheNodeHoldsItsLease(bool revoked)
    {
        // Arrange: holding the lease tells the leader the node saw a revocation written before its heartbeat; a slow
        // acknowledgement of another daemon, read first, must not leave the revoked or moved daemon running
        var pending = new ControllableDaemonAgent("pending");
        SetupNodeAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Active));
        var coordinator = CreateCoordinator([pending, agent]);
        await FollowerTickAsync(coordinator);
        DaemonAssignment[] assignments = revoked
            ? [MakeAssignment(nodeId, DaemonAssignmentState.Assigning, daemonId: "pending"), MakeAssignment(nodeId, DaemonAssignmentState.Revoking, generation: 2)]
            : [MakeAssignment(nodeId, DaemonAssignmentState.Assigning, daemonId: "pending")];
        mockAssignmentPersistence
            .Setup(p => p.GetNodeAssignmentsAsync(ServiceName, nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignments);
        mockAssignmentPersistence
            .Setup(p => p.AcknowledgeAsync(ServiceName, "pending", It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string _, long _, CancellationToken token) => NeverAsync<bool>(token));
        bool? runningWhenLeaseHeld = null;
        using var outOfTime = new CancellationTokenSource();

        // Act
        var tick = coordinator.OnHeartbeatTickAsync(
            nodeId,
            NotLeader,
            () =>
            {
                runningWhenLeaseHeld = agent.IsRunning;
                return heldLease;
            },
            outOfTime.Token);
        await hangReached.Task.WaitAsync(TimeSpan.FromSeconds(30));
        outOfTime.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tick.WaitAsync(TimeSpan.FromSeconds(30)));

        // Assert
        Assert.False(runningWhenLeaseHeld);
        Assert.False(agent.IsRunning);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenOneHalfOfTheTickThatCannotFinish_WhenTheTickRunsOutOfTime_ThenTheOtherHalfStillDidItsWork(bool nodeWorkHangs)
    {
        // Arrange: under a slow database the tick can end before all its work is done. A node's own work stops
        // runs it must not keep and restarts failed ones, and the leader places daemons; neither waits for the other.
        SetupNodes(Node(nodeId, [TestDaemonId, "other"]));
        SetupAssignments(ActiveOn(nodeId, TestDaemonId));
        SetupNodeAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Active));
        var coordinator = CreateCoordinator([agent]);
        await FollowerTickAsync(coordinator);
        agent.Crash();
        if (nodeWorkHangs)
        {
            mockAssignmentPersistence
                .Setup(p => p.GetNodeAssignmentsAsync(ServiceName, nodeId, It.IsAny<CancellationToken>()))
                .Returns((string _, Guid _, CancellationToken token) => NeverAsync<IReadOnlyList<DaemonAssignment>>(token));
        }
        else
        {
            mockAssignmentPersistence
                .Setup(p => p.GetAllAssignmentsAsync(ServiceName, It.IsAny<CancellationToken>()))
                .Returns((string _, CancellationToken token) => NeverAsync<IReadOnlyList<DaemonAssignment>>(token));
        }

        using var outOfTime = new CancellationTokenSource();

        // Act
        var tick = coordinator.OnHeartbeatTickAsync(nodeId, CancellationToken.None, () => heldLease, outOfTime.Token);
        await hangReached.Task.WaitAsync(TimeSpan.FromSeconds(30));
        outOfTime.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tick.WaitAsync(TimeSpan.FromSeconds(30)));

        // Assert
        Assert.Equal(nodeWorkHangs ? 1 : 2, agent.RunCount);
        mockAssignmentPersistence.Verify(
            p => p.AssignAsync(ServiceName, "other", nodeId, null, It.IsAny<CancellationToken>()),
            nodeWorkHangs ? Times.Once() : Times.Never());
    }

    [Theory]
    [InlineData(NewWork.Daemon, false)]
    [InlineData(NewWork.Daemon, true)]
    [InlineData(NewWork.Rebalance, false)]
    [InlineData(NewWork.Rebalance, true)]
    public async Task GivenNodeThatGaveUpADaemonItRan_WhenTheLeaderPlacesOrBalancesDaemons_ThenItGivesItNoneWhileAnotherNodeCanTakeThemUntilTheFailureCooldownElapsed(
        NewWork work,
        bool cooldownElapsed)
    {
        // Arrange: a node stops offering a daemon it keeps failing, for example one that cannot reach the systems
        // its daemons use; as the least busy node it would otherwise be given its other daemons to fail in turn
        var time = NewTime();
        var daemonOptions = new DaemonOptions();
        var failing = Guid.NewGuid();
        string[] running = ["o1", "o2", "o3"];
        SetupNodes(Node(failing, running), Node(nodeId, [TestDaemonId, .. running]));
        SetupAssignments([ActiveOn(failing, TestDaemonId), .. running.Select(d => ActiveOn(nodeId, d))]);
        var coordinator = CreateCoordinator([agent], daemonOptions, time);
        await LeaderTickAsync(coordinator);
        time.Advance(daemonOptions.FailureCooldown + TimeSpan.FromMilliseconds(cooldownElapsed ? 1 : -1));
        string[] declared = work == NewWork.Daemon ? ["new", .. running] : running;
        SetupNodes(Node(failing, declared), Node(nodeId, declared));
        SetupAssignments([.. running.Select(d => ActiveOn(nodeId, d))]);

        // Act
        await LeaderTickAsync(coordinator);

        // Assert
        if (work == NewWork.Daemon)
        {
            mockAssignmentPersistence.Verify(
                p => p.AssignAsync(ServiceName, "new", cooldownElapsed ? failing : nodeId, null, It.IsAny<CancellationToken>()),
                Times.Once);
        }
        else
        {
            mockAssignmentPersistence.Verify(
                p => p.RevokeAsync(ServiceName, It.Is<string>(d => running.Contains(d)), It.IsAny<long>(), It.IsAny<CancellationToken>()),
                cooldownElapsed ? Times.Once() : Times.Never());
        }
    }

    [Fact]
    public async Task GivenNodeWhoseLeaseRanOut_WhenItBrieflyOffersNoDaemon_ThenItIsNotTakenForANodeThatGaveOneUp()
    {
        // Arrange: a node whose lease ran out, for example during a database outage, offers no daemon until it
        // holds a lease again; that says nothing about whether it can run them
        var time = NewTime();
        var recovering = Guid.NewGuid();
        string[] running = ["o1", "o2", "o3"];
        SetupNodes(Node(recovering, []), Node(nodeId, ["new", .. running]));
        SetupAssignments([ActiveOn(recovering, TestDaemonId), .. running.Select(d => ActiveOn(nodeId, d))]);
        var coordinator = CreateCoordinator([agent], timeProvider: time);
        await LeaderTickAsync(coordinator);
        SetupNodes(Node(recovering, ["new", .. running]), Node(nodeId, ["new", .. running]));
        SetupAssignments([.. running.Select(d => ActiveOn(nodeId, d))]);

        // Act
        await LeaderTickAsync(coordinator);

        // Assert
        mockAssignmentPersistence.Verify(
            p => p.AssignAsync(ServiceName, "new", recovering, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // --- Leader view ---

    [Fact]
    public async Task GivenNodeWhoseRegistrationWasReplacedDuringTheRead_WhenItIsReadTwice_ThenTheLeaderStillPlacesItsDaemons()
    {
        // Arrange: a registration removed and written again while a database scans the service's nodes can be
        // returned once as it was and once as it became; both are true of the node at some point of the read
        SetupNodes(Node(nodeId, [TestDaemonId]), Node(nodeId, [TestDaemonId]));
        SetupAssignments();
        var coordinator = CreateCoordinator([agent]);

        // Act
        await LeaderTickAsync(coordinator);

        // Assert
        mockAssignmentPersistence.Verify(
            p => p.AssignAsync(ServiceName, TestDaemonId, nodeId, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GivenNodeThatRegistersAndReceivesTheDaemonWhileTheLeaderReads_WhenReconciling_ThenTheDaemonIsNotTakenFromIt()
    {
        // Arrange: between the leader's two reads, a node registers and a newer leader assigns it the daemon
        var newcomer = Guid.NewGuid();
        var assignedToNewcomer = ActiveOn(newcomer, TestDaemonId);
        SetupAssignments();
        mockLeaderElectionPersistence
            .Setup(p => p.GetNodesAsync(ServiceName, It.IsAny<CancellationToken>()))
            .Callback(() => mockAssignmentPersistence
                .Setup(p => p.GetAllAssignmentsAsync(ServiceName, It.IsAny<CancellationToken>()))
                .ReturnsAsync([assignedToNewcomer]))
            .ReturnsAsync([Node(Guid.NewGuid(), [TestDaemonId])]);
        var coordinator = CreateCoordinator([]);

        // Act
        await LeaderTickAsync(coordinator);

        // Assert
        mockAssignmentPersistence.Verify(
            p => p.AssignAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), assignedToNewcomer, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(LeadershipLost.BeforeTheTick)]
    [InlineData(LeadershipLost.DuringTheRead)]
    [InlineData(LeadershipLost.DuringAReadThatFails)]
    public async Task GivenStateALeaderWouldActOn_WhenTheNodeDoesNotLeadOrStopsLeadingWhileReading_ThenItWritesNothingAndTheNodeCarriesOn(LeadershipLost lost)
    {
        // Arrange: a declared daemon nobody runs and a daemon on a node that is gone. The lease can run out while
        // the leader is reading, for example during a long pause, and the interrupted read may then fail with
        // whatever the database driver throws.
        using var leadership = new CancellationTokenSource();
        if (lost == LeadershipLost.BeforeTheTick)
        {
            await leadership.CancelAsync();
        }

        SetupAssignments(MakeAssignment(Guid.NewGuid(), DaemonAssignmentState.Active, daemonId: "other"));
        var nodesRead = mockLeaderElectionPersistence
            .Setup(p => p.GetNodesAsync(ServiceName, It.IsAny<CancellationToken>()))
            .Callback(leadership.Cancel);
        if (lost == LeadershipLost.DuringAReadThatFails)
        {
            nodesRead.ThrowsAsync(new IOException("Connection closed."));
        }
        else
        {
            nodesRead.ReturnsAsync([Node(nodeId, [TestDaemonId, "other", "unassigned"])]);
        }

        SetupNodeAssignments(MakeAssignment(nodeId, DaemonAssignmentState.Active));
        var coordinator = CreateCoordinator([agent]);

        // Act
        await coordinator.OnHeartbeatTickAsync(nodeId, leadership.Token, () => heldLease, CancellationToken.None);

        // Assert
        VerifyNoAssignment();
        VerifyNoRevocation();
        mockAssignmentPersistence.Verify(
            p => p.RemoveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.True(agent.IsRunning);
    }

    // --- Rebalancing ---

    [Fact]
    public async Task GivenOneNodeRunningThreeDaemonsAndAnIdleNode_WhenTheIdleNodeIsSeenRenewing_ThenOneDaemonIsRevokedFromTheBusyNodeAndNotBefore()
    {
        // Arrange: a registration written once, such as by a heartbeat that reached the database after its node
        // shut down, must not draw daemons away from a node running them
        var busy = Guid.NewGuid();
        var idle = Guid.NewGuid();
        var coordinator = CreateCoordinator([]);
        SetupNodes(Node(busy, ["x", "y", "z"]), Node(idle, ["x", "y", "z"]));
        SetupAssignments(ActiveOn(busy, "x"), ActiveOn(busy, "y"), ActiveOn(busy, "z"));

        // Act
        await LeaderTickAsync(coordinator);

        // Assert
        VerifyNoRevocation();

        // Act
        await LeaderTickAsync(coordinator);

        // Assert
        mockAssignmentPersistence.Verify(
            p => p.RevokeAsync(ServiceName, It.IsIn("x", "y", "z"), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyNoAssignment();
        mockObserver.Verify(o => o.OnDaemonRevokedAsync(It.IsIn("x", "y", "z"), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenNodeJudgedDeadButNotRemovedHoldingDaemonsNoLiveNodeDeclares_WhenTheClusterIsUneven_ThenItIsStillRebalanced()
    {
        // Arrange: the node judged dead is the busiest, so its daemons are looked at first
        var time = NewTime();
        var dead = Guid.NewGuid();
        var busy = Guid.NewGuid();
        var idle = Guid.NewGuid();
        var deadNode = Node(dead, ["d1", "d2", "d3"], renews: false);
        SetupNodes(deadNode, Node(busy, ["x", "y"]), Node(idle, ["x", "y"]));
        SetupAssignments(ActiveOn(dead, "d1"), ActiveOn(dead, "d2"), ActiveOn(dead, "d3"), ActiveOn(busy, "x"), ActiveOn(busy, "y"));
        mockLeaderElectionPersistence
            .Setup(p => p.RemoveNodesAsync(ServiceName, It.IsAny<IReadOnlyCollection<NodeRegistration>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var coordinator = CreateCoordinator([], timeProvider: time);
        await FollowerTickAsync(coordinator);
        time.Advance(deadNode.RegistrationTtl);

        // Act
        await LeaderTickAsync(coordinator);

        // Assert
        mockAssignmentPersistence.Verify(
            p => p.RevokeAsync(ServiceName, It.IsIn("x", "y"), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(NoRebalance.LoadDifferenceOfOne)]
    [InlineData(NoRebalance.AnAssignmentStillInTransition)]
    [InlineData(NoRebalance.IdleNodeDoesNotDeclareTheDaemons)]
    [InlineData(NoRebalance.IdleNodeIsNotResponsive)]
    public async Task GivenUnevenClusterThatMustNotBeRebalanced_WhenReconcile_ThenRevokesNothing(NoRebalance reason)
    {
        // Arrange
        var busy = Guid.NewGuid();
        var idle = Guid.NewGuid();
        string[] daemons = ["x", "y", "z"];
        SetupNodes(
            Node(busy, daemons),
            Node(
                idle,
                reason == NoRebalance.IdleNodeDoesNotDeclareTheDaemons ? [] : daemons,
                renews: reason != NoRebalance.IdleNodeIsNotResponsive));
        SetupAssignments(
            ActiveOn(busy, "x"),
            ActiveOn(busy, "y"),
            reason switch
            {
                NoRebalance.LoadDifferenceOfOne => ActiveOn(idle, "z"),
                NoRebalance.AnAssignmentStillInTransition => new DaemonAssignment("z", busy, 1, DaemonAssignmentState.Assigning, DateTime.UtcNow),
                _ => ActiveOn(busy, "z"),
            });
        var coordinator = CreateCoordinator([]);
        await FollowerTickAsync(coordinator);

        // Act
        await LeaderTickAsync(coordinator);

        // Assert
        VerifyNoRevocation();
    }

    // --- Silent nodes ---

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task GivenNodeWhoseHeartbeatStopsChanging_WhenItsRegistrationTtlElapsesSinceThisNodeFirstSawItAsAFollower_ThenItIsRemovedAndItsDaemonMovedButNotBefore(
        int wallClockSteps)
    {
        // Arrange: a node judges another from whether its heartbeat changes, on its own monotonic clock, so neither a
        // time the database records nor a step in its wall clock moves the moment it is judged dead. Every node
        // observes, so a node that becomes leader judges from when it first saw the heartbeat as a follower.
        var silent = Guid.NewGuid();
        var other = Guid.NewGuid();
        var silentNode = Node(silent, [TestDaemonId], renews: false);
        SetupNodes(silentNode, Node(other, [TestDaemonId]));
        SetupAssignments(ActiveOn(silent, TestDaemonId));
        var time = new SteppingClock(NewTime().GetUtcNow());
        var coordinator = CreateCoordinator([], timeProvider: time);
        mockLeaderElectionPersistence
            .Setup(p => p.RemoveNodesAsync(ServiceName, It.IsAny<IReadOnlyCollection<NodeRegistration>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([silent]);
        await FollowerTickAsync(coordinator);
        time.Advance(silentNode.RegistrationTtl - TimeSpan.FromMilliseconds(1));
        time.StepWallClock(silentNode.RegistrationTtl * wallClockSteps);

        // Act
        await LeaderTickAsync(coordinator);

        // Assert
        VerifyNoAssignment();

        // Act
        time.Advance(TimeSpan.FromMilliseconds(1));
        await LeaderTickAsync(coordinator);

        // Assert
        mockLeaderElectionPersistence.Verify(
            p => p.RemoveNodesAsync(ServiceName, new[] { silentNode }, It.IsAny<CancellationToken>()),
            Times.Once);
        mockLeaderElectionObserver.Verify(o => o.OnNodeRemovedAsync(silent, It.IsAny<CancellationToken>()), Times.Once);
        mockAssignmentPersistence.Verify(
            p => p.AssignAsync(ServiceName, TestDaemonId, other, It.IsAny<DaemonAssignment?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GivenNodeReadUnrenewedJustUnderItsTtl_WhenTheLeaderHandlesTheReadLater_ThenTheNodeIsNotJudgedDead()
    {
        // Arrange: a read shows the node had not renewed by the time it was issued, not by the time it is handled,
        // and the node may have renewed since without releasing its daemons
        var silent = Guid.NewGuid();
        var silentNode = Node(silent, [TestDaemonId], renews: false);
        SetupNodes(silentNode, Node(Guid.NewGuid(), [TestDaemonId]));
        SetupAssignments(ActiveOn(silent, TestDaemonId));
        var time = NewTime();
        var coordinator = CreateCoordinator([], timeProvider: time);
        await FollowerTickAsync(coordinator);
        time.Advance(silentNode.RegistrationTtl - TimeSpan.FromMilliseconds(1));
        mockAssignmentPersistence
            .Setup(p => p.GetAllAssignmentsAsync(ServiceName, It.IsAny<CancellationToken>()))
            .Callback(() => time.Advance(TimeSpan.FromSeconds(1)))
            .ReturnsAsync([ActiveOn(silent, TestDaemonId)]);

        // Act
        await LeaderTickAsync(coordinator);

        // Assert
        mockLeaderElectionPersistence.Verify(
            p => p.RemoveNodesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<NodeRegistration>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNoAssignment();
    }

    [Fact]
    public async Task GivenNodeJudgedDead_WhenItsRegistrationIsNotRemovedBecauseItRenewedSinceTheRead_ThenItKeepsItsDaemonsButIsGivenNoNewOne()
    {
        // Arrange: only that node declares the unplaced daemon
        var silent = Guid.NewGuid();
        var silentNode = Node(silent, [TestDaemonId, "unplaced"], renews: false);
        SetupNodes(silentNode, Node(Guid.NewGuid(), [TestDaemonId]));
        SetupAssignments(ActiveOn(silent, TestDaemonId));
        var time = NewTime();
        var coordinator = CreateCoordinator([], timeProvider: time);
        mockLeaderElectionPersistence
            .Setup(p => p.RemoveNodesAsync(ServiceName, It.IsAny<IReadOnlyCollection<NodeRegistration>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        await FollowerTickAsync(coordinator);
        time.Advance(silentNode.RegistrationTtl);

        // Act
        await LeaderTickAsync(coordinator);

        // Assert
        VerifyNoAssignment();
        VerifyNoRevocation();
    }

    // --- Helpers ---

    /// <summary>
    /// Returns the daemons the node's next heartbeat declares, as the worker does when it builds the heartbeat.
    /// </summary>
    private static IReadOnlyList<string> DeclareDaemons(DaemonCoordinator coordinator) =>
        coordinator.GetDeclaredDaemonIds(canRunDaemons: true);

    private static FakeTimeProvider NewTime() => new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    private static DaemonAssignment ActiveOn(Guid node, string daemonId) =>
        MakeAssignment(node, DaemonAssignmentState.Active, daemonId: daemonId);

    private void SetupNodeAssignments(params DaemonAssignment[] assignments)
    {
        mockAssignmentPersistence
            .Setup(p => p.GetNodeAssignmentsAsync(ServiceName, nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignments);
        mockAssignmentPersistence
            .Setup(p => p.AcknowledgeAsync(ServiceName, TestDaemonId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private Task LeaderTickAsync(DaemonCoordinator coordinator) =>
        coordinator.OnHeartbeatTickAsync(nodeId, leadershipToken: CancellationToken.None, () => heldLease, CancellationToken.None);

    private Task FollowerTickAsync(DaemonCoordinator coordinator) =>
        coordinator.OnHeartbeatTickAsync(nodeId, leadershipToken: NotLeader, () => heldLease, CancellationToken.None);

    private void VerifyNoAssignment() =>
        mockAssignmentPersistence.Verify(
            p => p.AssignAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DaemonAssignment?>(), It.IsAny<CancellationToken>()),
            Times.Never);

    private void VerifyNoRevocation() =>
        mockAssignmentPersistence.Verify(
            p => p.RevokeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);

    /// <summary>
    /// Registers another node of the service that offers <paramref name="daemonId"/>, which this node can give it up to.
    /// </summary>
    private void OfferedByAnotherNode(string daemonId)
    {
        SetupNodes(Node(nodeId, [daemonId]), Node(Guid.NewGuid(), [daemonId]));
        mockAssignmentPersistence
            .Setup(p => p.GetAllAssignmentsAsync(ServiceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    /// <summary>
    /// Makes the nodes' registrations readable. Each read shows a new heartbeat of every node, except silent ones.
    /// </summary>
    private void SetupNodes(params NodeRegistration[] nodes)
    {
        mockLeaderElectionPersistence
            .Setup(p => p.GetNodesAsync(ServiceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [.. nodes.Select(n => silentNodes.Contains(n) ? n : n with { HeartbeatId = Guid.NewGuid() })]);
    }

    /// <summary>
    /// Returns a node's registration. A node that renews shows a new heartbeat on every read; one that does not
    /// shows the same one.
    /// </summary>
    private NodeRegistration Node(Guid id, IReadOnlyList<string> daemonIds, bool renews = true, TimeSpan? registrationTtl = null)
    {
        var node = new NodeRegistration(id, daemonIds, Guid.NewGuid(), registrationTtl ?? new LeaderElectionOptions().NodeRegistrationTtl);
        if (!renews)
        {
            silentNodes.Add(node);
        }

        return node;
    }

    private void SetupAssignments(params DaemonAssignment[] assignments)
    {
        mockAssignmentPersistence
            .Setup(p => p.GetAllAssignmentsAsync(ServiceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignments);
        mockAssignmentPersistence
            .Setup(p => p.GetNodeAssignmentsAsync(ServiceName, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        mockAssignmentPersistence
            .Setup(p => p.RevokeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string daemonId, long _, CancellationToken _) =>
                new DaemonAssignment(daemonId, Guid.NewGuid(), 2, DaemonAssignmentState.Revoking, DateTime.UtcNow));
        mockAssignmentPersistence
            .Setup(p => p.AssignAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DaemonAssignment?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string daemonId, Guid target, DaemonAssignment? _, CancellationToken _) =>
                new DaemonAssignment(daemonId, target, 2, DaemonAssignmentState.Assigning, DateTime.UtcNow));
    }

    private static DaemonAssignment MakeAssignment(
        Guid assignedNodeId,
        DaemonAssignmentState state,
        long generation = 1,
        string daemonId = TestDaemonId) =>
        new(daemonId, assignedNodeId, generation, state, DateTime.UtcNow);

    /// <summary>
    /// A database call that never returns. Signals <see cref="hangReached"/>, so a test runs the tick out of time
    /// only once the call is under way, whether or not the coordinator yields before it.
    /// </summary>
    private async Task<T> NeverAsync<T>(CancellationToken token)
    {
        hangReached.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        throw new InvalidOperationException("Unreachable.");
    }

    private DaemonCoordinator CreateCoordinator(
        IEnumerable<IDaemonAgent> agents,
        DaemonOptions? daemonOptions = null,
        TimeProvider? timeProvider = null,
        TaskScheduler? runScheduler = null)
    {
        return TestNodes.CreateCoordinator(
            mockAssignmentPersistence.Object,
            mockLeaderElectionPersistence.Object,
            agents,
            daemonOptions ?? new DaemonOptions(),
            new LeaderElectionOptions(),
            Mock.Of<INodeIdentity>(n => n.NodeId == nodeId && n.ServiceName == ServiceName),
            timeProvider ?? NewTime(),
            runScheduler ?? InlineTaskScheduler.Instance,
            [mockObserver.Object],
            [mockLeaderElectionObserver.Object]);
    }

    public enum LeadershipLost
    {
        BeforeTheTick,
        DuringTheRead,
        DuringAReadThatFails,
    }

    public enum NewWork
    {
        Daemon,
        Rebalance,
    }

    public enum RunEnd
    {
        LeaseRunsOut,
        NewGenerationAssigned,
        NewGenerationActive,
    }

    public enum Outcome
    {
        Moved,
        Revoked,
        Kept,
    }

    public enum Placement
    {
        OnlyOnADeclaringNode,
        OnTheLeastLoadedNode,
        OnAResponsiveNodeBeforeALessLoadedSilentOne,
        OnASilentNodeWhenNoneIsResponsive,
    }

    public enum NoRebalance
    {
        LoadDifferenceOfOne,
        AnAssignmentStillInTransition,
        IdleNodeDoesNotDeclareTheDaemons,
        IdleNodeIsNotResponsive,
    }

    public enum InvalidDaemons
    {
        EmptyId,
        BlankId,
        IdLongerThanTheLimit,
        TwoWithTheSameId,
    }

    public enum DaemonBlocks
    {
        WhenStarted,
        WhenStoppedByTheNode,
        WhenFenced,
    }

    /// <summary>
    /// Daemon that blocks the thread starting it, or the thread cancelling its token, until released.
    /// </summary>
    private sealed class BlockingDaemonAgent(string daemonId, ManualResetEventSlim release, bool blocksWhileStopping) : IDaemonAgent
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string DaemonId { get; } = daemonId;

        public Task Started => started.Task;

        public Task RunAsync(DaemonRunContext context)
        {
            var assignmentToken = context.AssignmentToken;
            if (blocksWhileStopping)
            {
                assignmentToken.Register(() => release.Wait(CancellationToken.None));
            }
            else
            {
                release.Wait(CancellationToken.None);
            }

            started.TrySetResult();
            return Task.Delay(Timeout.InfiniteTimeSpan, assignmentToken);
        }
    }
}

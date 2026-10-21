namespace Emit.UnitTests.LeaderElection;

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

public class HeartbeatWorkerTests
{
    private const string ServiceName = "test-service";
    private const string DaemonId = "a";

    private readonly Mock<ILeaderElectionPersistence> mockPersistence = new();
    private readonly Mock<ILeaderElectionObserver> mockObserver = new();
    private readonly Mock<IDaemonAssignmentPersistence> mockAssignmentPersistence = new();
    private readonly List<Guid> sentHeartbeatIds = [];
    private readonly SteppingClock time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly LeaderElectionOptions options = new()
    {
        HeartbeatInterval = TimeSpan.FromSeconds(1),
        QueryTimeout = TimeSpan.FromMilliseconds(150),
        LeaseDuration = TimeSpan.FromSeconds(4),
        NodeRegistrationTtl = TimeSpan.FromSeconds(7)
    };

    public HeartbeatWorkerTests()
    {
        // A node has no assignments until a test gives it some, as a real store answers
        mockAssignmentPersistence
            .Setup(p => p.GetNodeAssignmentsAsync(ServiceName, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    // --- Leadership ---

    [Fact]
    public async Task GivenNewNode_WhenHeartbeatsGrantTheLease_ThenItRegistersWithItsServiceAndBecomesLeaderNotifyingOnce()
    {
        // Arrange
        var worker = CreateWorker();
        List<HeartbeatRequest> requests = [];
        mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .Callback((HeartbeatRequest request, CancellationToken _) => requests.Add(request))
            .ReturnsAsync((HeartbeatRequest request, CancellationToken _) => new HeartbeatResult(true, request.NodeId, request.HeartbeatId, options.LeaseDuration));
        Assert.False(worker.IsLeader);
        Assert.True(worker.LeadershipToken.IsCancellationRequested);

        // Act
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        await worker.PerformHeartbeatAsync(CancellationToken.None);

        // Assert
        Assert.True(worker.IsLeader);
        Assert.False(worker.LeadershipToken.IsCancellationRequested);
        Assert.All(requests, r => Assert.Equal(
            (ServiceName, worker.NodeId, options.LeaseDuration, options.NodeRegistrationTtl),
            (r.ServiceName, r.NodeId, r.LeaseDuration, r.NodeTtl)));
        mockObserver.Verify(o => o.OnNodeRegisteredAsync(worker.NodeId, It.IsAny<CancellationToken>()), Times.Once);
        mockObserver.Verify(o => o.OnLeaderElectedAsync(worker.NodeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenNewNodeThatCannotReadItsAssignments_WhenItHeartbeats_ThenItOffersItsDaemonsOnItsFirstHeartbeatAndThenOnlyOnceItHoldsALease()
    {
        // Arrange: a node is presumed able to run daemons until it tries; one that cannot read its assignments
        // could not start a daemon it was given, and the leader would keep moving daemons to it
        var worker = CreateWorker(agents: [new ControllableDaemonAgent(DaemonId)]);
        var reads = 0;
        mockAssignmentPersistence.Setup(p => p.GetNodeAssignmentsAsync(ServiceName, worker.NodeId, It.IsAny<CancellationToken>()))
            .Returns(() => ++reads <= 2
                ? Task.FromException<IReadOnlyList<DaemonAssignment>>(new TimeoutException("No response."))
                : Task.FromResult<IReadOnlyList<DaemonAssignment>>([]));
        List<HeartbeatRequest> requests = [];
        mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .Callback((HeartbeatRequest request, CancellationToken _) => requests.Add(request))
            .ReturnsAsync((HeartbeatRequest request, CancellationToken _) => new HeartbeatResult(false, Guid.NewGuid(), Guid.NewGuid(), options.LeaseDuration));

        // Act
        for (var i = 0; i < 4; i++)
        {
            await worker.PerformHeartbeatAsync(CancellationToken.None);
        }

        // Assert
        Assert.Equal([[DaemonId], [], [], [DaemonId]], requests.Select(r => r.DaemonIds));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenLeader_WhenTheNextHeartbeatFailsOrAnotherNodeLeads_ThenLosesLeadership(bool heartbeatFails)
    {
        // Arrange
        SetupHeartbeat(isLeader: true);
        var worker = CreateWorker();
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        var token = worker.LeadershipToken;
        if (heartbeatFails)
        {
            mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Database unreachable."));
        }
        else
        {
            SetupHeartbeat(isLeader: false);
        }

        // Act
        await worker.PerformHeartbeatAsync(CancellationToken.None);

        // Assert
        Assert.False(worker.IsLeader);
        Assert.True(token.IsCancellationRequested);
        mockObserver.Verify(o => o.OnLeaderLostAsync(worker.NodeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task GivenCancellationCallbackThatThrows_WhenLeadershipAndDaemonsAreCancelled_ThenTheNodeCarriesOn(
        bool onLeadershipToken,
        bool byLeaseExpiry)
    {
        // Arrange: callbacks on the public leadership token and on daemon tokens are code Emit does not control,
        // and a lease that runs out cancels them from a timer, where an escaping exception would end the process
        var agent = new ControllableDaemonAgent(DaemonId);
        SetupHeartbeat(isLeader: true);
        var worker = CreateWorker(agents: [agent]);
        SetupActiveAssignment(worker.NodeId);
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        var token = onLeadershipToken ? worker.LeadershipToken : agent.LastToken;
        token.Register(() => throw new InvalidOperationException("Callback failed."));

        // Act
        if (byLeaseExpiry)
        {
            time.Advance(options.LeaseDuration);
        }
        else
        {
            mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Database unreachable."));
            await worker.PerformHeartbeatAsync(CancellationToken.None);
        }

        SetupHeartbeat(isLeader: true);
        await worker.PerformHeartbeatAsync(CancellationToken.None);

        // Assert
        Assert.True(token.IsCancellationRequested);
        Assert.True(worker.IsLeader);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenLeadershipCallbackThatBlocks_WhenLeadershipEnds_ThenNeitherTheHeartbeatNorTheLeaseTimerWaitsForIt(bool byLeaseExpiry)
    {
        // Arrange: application code gated on the leadership token, such as cleanup after awaiting it, runs on
        // the thread that cancels the token
        using var release = new ManualResetEventSlim();
        SetupHeartbeat(isLeader: true);
        var worker = CreateWorker(callbackScheduler: TaskScheduler.Default);
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        worker.LeadershipToken.Register(() => release.Wait(CancellationToken.None));

        try
        {
            // Act
            var leadershipEnds = byLeaseExpiry
                ? Task.Run(() => time.Advance(options.LeaseDuration))
                : Task.Run(() =>
                {
                    mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new HeartbeatResult(IsLeader: false, LeaderNodeId: Guid.NewGuid(), LeaseHeartbeatId: Guid.NewGuid(), options.LeaseDuration));
                    return worker.PerformHeartbeatAsync(CancellationToken.None);
                });

            // Assert
            await leadershipEnds.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(worker.IsLeader);
            Assert.True(SpinWait.SpinUntil(() => worker.LeadershipToken.IsCancellationRequested, TimeSpan.FromSeconds(30)));
        }
        finally
        {
            release.Set();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenHeartbeatsThatFail_WhenTheFirstMayHaveRenewedTheLeaseBeforeFailing_ThenTheNodeResignsALeaseWrittenByItOrTheOneTheLastSuccessReportedOnly(
        bool leaseWrittenByALateHeartbeat)
    {
        // Arrange: the lease compare-and-swap can succeed before a later step of the heartbeat times out, and an
        // earlier heartbeat that timed out can reach the database late and write the lease. Naming the heartbeats
        // keeps a resign that reaches the database late from releasing a lease renewed since. The heartbeats after
        // the first failure do not claim the lease, so they cannot have written it however long the outage lasts.
        var lateHeartbeat = Guid.NewGuid();
        SetupHeartbeat(isLeader: true);
        if (leaseWrittenByALateHeartbeat)
        {
            mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
                .Callback((HeartbeatRequest request, CancellationToken _) => sentHeartbeatIds.Add(request.HeartbeatId))
                .ReturnsAsync((HeartbeatRequest request, CancellationToken _) => new HeartbeatResult(true, request.NodeId, lateHeartbeat, options.LeaseDuration));
        }

        var worker = CreateWorker();
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        var leaseWriter = leaseWrittenByALateHeartbeat ? lateHeartbeat : sentHeartbeatIds.Single();
        mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .Callback((HeartbeatRequest request, CancellationToken _) => sentHeartbeatIds.Add(request.HeartbeatId))
            .ThrowsAsync(new TimeoutException());

        IReadOnlyCollection<Guid>? resigned = null;
        mockPersistence
            .Setup(p => p.ResignLeadershipAsync(ServiceName, worker.NodeId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .Callback((string _, Guid _, IReadOnlyCollection<Guid> ids, CancellationToken _) => resigned = ids)
            .Returns(Task.CompletedTask);

        // Act
        for (var i = 0; i < 3; i++)
        {
            await worker.PerformHeartbeatAsync(CancellationToken.None);
        }

        // Assert
        var (lastSucceeded, firstFailed) = (sentHeartbeatIds[0], sentHeartbeatIds[1]);
        Assert.Equal(new HashSet<Guid> { lastSucceeded, leaseWriter, firstFailed }, resigned!.ToHashSet());
    }

    [Fact]
    public async Task GivenLeaseWrittenByAHeartbeatThatFailed_WhenTheNextHeartbeatOnlyReportsIt_ThenTheNodeStillResignsIt()
    {
        // Arrange: a heartbeat that does not claim the lease reports the one recorded for this node without leading,
        // and a resign that leaves it out would keep other nodes waiting a whole lease
        var failedWriter = Guid.NewGuid();
        SetupHeartbeat(isLeader: false);
        var worker = CreateWorker();
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .Callback((HeartbeatRequest request, CancellationToken _) => failedWriter = request.HeartbeatId)
            .ThrowsAsync(new TimeoutException());
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HeartbeatRequest request, CancellationToken _) => new HeartbeatResult(false, request.NodeId, failedWriter, options.LeaseDuration));
        await worker.PerformHeartbeatAsync(CancellationToken.None);

        IReadOnlyCollection<Guid>? resignedOnShutdown = null;
        mockPersistence
            .Setup(p => p.ResignLeadershipAsync(ServiceName, worker.NodeId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .Callback((string _, Guid _, IReadOnlyCollection<Guid> ids, CancellationToken _) => resignedOnShutdown = ids)
            .Returns(Task.CompletedTask);

        // Act
        await worker.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.Contains(failedWriter, resignedOnShutdown!);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenLeadershipTokenNotYetCancelled_WhenTheLeaderResignsAfterAFailedHeartbeat_ThenItResignsOnlyOnceTheTokenReadsCancelledAndBeforeTheLeaseRunsOut(
        bool cancelledInTime)
    {
        // Arrange: the token is cancelled on the callback scheduler, which can be slow to get to it, and once the
        // lease is given up another node can lead while code holding the token still sees it valid
        var callbacks = new QueuedTaskScheduler();
        SetupHeartbeat(isLeader: true);
        var worker = CreateWorker(callbackScheduler: callbacks);
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        var leadership = worker.LeadershipToken;
        mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException());

        // Act
        var failedHeartbeat = worker.PerformHeartbeatAsync(CancellationToken.None);

        // Assert
        Assert.False(failedHeartbeat.IsCompleted);
        mockPersistence.Verify(
            p => p.ResignLeadershipAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Act
        if (cancelledInTime)
        {
            await Task.Run(callbacks.RunQueued);
        }
        else
        {
            time.Advance(options.LeaseDuration);
        }

        await failedHeartbeat.WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.Equal(cancelledInTime, leadership.IsCancellationRequested);
        mockPersistence.Verify(
            p => p.ResignLeadershipAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            cancelledInTime ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task GivenHeartbeatsThatFail_WhenTheNextOnesAreSent_ThenTheNodeClaimsTheLeaseOnlyOnceItsHeartbeatsSucceededForALeaseDuration()
    {
        // Arrange: a heartbeat that fails on the node may have renewed the lease. A node whose link to the database
        // keeps failing would otherwise win the lease back between failures, and its renewals would keep other nodes
        // from ever seeing it go unrenewed while it can hardly lead.
        List<bool> claims = [];
        var fails = false;
        mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .Returns((HeartbeatRequest request, CancellationToken _) =>
            {
                claims.Add(request.ClaimsLease);
                return fails
                    ? Task.FromException<HeartbeatResult>(new TimeoutException())
                    : Task.FromResult(new HeartbeatResult(request.ClaimsLease, request.NodeId, request.HeartbeatId, options.LeaseDuration));
            });
        mockAssignmentPersistence.Setup(p => p.GetAllAssignmentsAsync(ServiceName, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        mockPersistence.Setup(p => p.GetNodesAsync(ServiceName, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var worker = CreateWorker();
        await worker.PerformHeartbeatAsync(CancellationToken.None);

        // Act: a failure, a success, another failure, then successes for a lease duration
        foreach (var heartbeatFails in new[] { true, false, true })
        {
            time.Advance(options.HeartbeatInterval);
            fails = heartbeatFails;
            await worker.PerformHeartbeatAsync(CancellationToken.None);
        }

        fails = false;
        for (var elapsed = TimeSpan.Zero; elapsed < options.LeaseDuration; elapsed += options.HeartbeatInterval)
        {
            time.Advance(options.HeartbeatInterval);
            await worker.PerformHeartbeatAsync(CancellationToken.None);
        }

        // Assert
        var heartbeatsInALease = (int)(options.LeaseDuration / options.HeartbeatInterval);
        Assert.Equal([true, true, false, false, .. Enumerable.Repeat(false, heartbeatsInALease - 1), true], claims);
        Assert.True(worker.IsLeader);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenHeartbeatLongerThanOneQueryTimeout_WhenItEndsAroundTheHeartbeatTimeout_ThenItSucceedsOnlyWithinIt(
        bool withinThreeQueryTimeouts)
    {
        // Arrange: a heartbeat makes up to three database calls and is bounded by three query timeouts; one query
        // timeout would fail every heartbeat that claims the lease while each of its calls is within its own
        var elapsed = withinThreeQueryTimeouts ? options.HeartbeatTimeout - TimeSpan.FromMilliseconds(1) : options.HeartbeatTimeout;
        mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (HeartbeatRequest request, CancellationToken token) =>
            {
                time.Advance(elapsed);
                await Task.Yield();
                token.ThrowIfCancellationRequested();
                return new HeartbeatResult(true, request.NodeId, request.HeartbeatId, options.LeaseDuration);
            });
        var worker = CreateWorker();

        // Act
        await worker.PerformHeartbeatAsync(CancellationToken.None);

        // Assert
        Assert.Equal(withinThreeQueryTimeouts, worker.IsLeader);
    }

    [Fact]
    public async Task GivenLeaderThatCannotObserveItsService_WhenThatLastsALease_ThenItStopsClaimingTheLeaseAndGivesItUpUntilItObservesAgain()
    {
        // Arrange: a leader that cannot read its service's nodes and assignments places no daemon, and one whose
        // heartbeats still succeed would otherwise keep leading forever; a shorter blip must not cost the lease
        var worker = CreateWorker();
        List<bool> claims = [];
        mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .Callback((HeartbeatRequest request, CancellationToken _) => claims.Add(request.ClaimsLease))
            .ReturnsAsync((HeartbeatRequest request, CancellationToken _) =>
                new HeartbeatResult(request.ClaimsLease, worker.NodeId, request.HeartbeatId, options.LeaseDuration));
        mockAssignmentPersistence
            .Setup(p => p.GetAllAssignmentsAsync(ServiceName, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("No response."));
        var heartbeatsInALease = (int)(options.LeaseDuration / options.HeartbeatInterval);

        // Act
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        for (var i = 0; i < heartbeatsInALease; i++)
        {
            time.Advance(options.HeartbeatInterval);
            await worker.PerformHeartbeatAsync(CancellationToken.None);
        }

        // Assert
        Assert.Equal([.. Enumerable.Repeat(true, heartbeatsInALease), false], claims);
        Assert.False(worker.IsLeader);
        mockPersistence.Verify(
            p => p.ResignLeadershipAsync(ServiceName, worker.NodeId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Act
        mockAssignmentPersistence
            .Setup(p => p.GetAllAssignmentsAsync(ServiceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        mockPersistence.Setup(p => p.GetNodesAsync(ServiceName, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        for (var i = 0; i < 2; i++)
        {
            time.Advance(options.HeartbeatInterval);
            await worker.PerformHeartbeatAsync(CancellationToken.None);
        }

        // Assert
        Assert.Equal([false, true], claims[^2..]);
        Assert.True(worker.IsLeader);
    }

    [Fact]
    public async Task GivenDatabaseClientThatIgnoresCancellation_WhenAHeartbeatNeverReturns_ThenItFailsAtTheHeartbeatTimeout()
    {
        // Arrange: a client that does not honour cancellation must not hang the node, which could then neither
        // retry nor shut down
        mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource<HeartbeatResult>().Task);
        var worker = CreateWorker();

        // Act
        var heartbeat = worker.PerformHeartbeatAsync(CancellationToken.None);
        time.Advance(options.HeartbeatTimeout);

        // Assert
        await heartbeat.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(worker.IsLeader);
    }

    [Fact]
    public async Task GivenDurationsWithAFractionOfAMillisecond_WhenLeading_ThenTheyAreSentInWholeMilliseconds()
    {
        // Arrange: a database stores the lease in whole milliseconds, and other nodes wait what it stores before
        // taking it over
        options.LeaseDuration = TimeSpan.FromSeconds(4) + TimeSpan.FromTicks(5_000);
        options.NodeRegistrationTtl = TimeSpan.FromSeconds(7) + TimeSpan.FromTicks(5_000);
        HeartbeatRequest? sent = null;
        mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .Callback((HeartbeatRequest request, CancellationToken _) => sent = request)
            .ReturnsAsync((HeartbeatRequest request, CancellationToken _) => new HeartbeatResult(true, request.NodeId, request.HeartbeatId, request.LeaseDuration));
        var worker = CreateWorker();

        // Act
        await worker.PerformHeartbeatAsync(CancellationToken.None);

        // Assert
        Assert.Equal((TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(7)), (sent!.LeaseDuration, sent.NodeTtl));
    }

    // --- Lease and fencing: leadership and daemons both end when the lease would expire ---

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GivenLeaderRunningADaemon_WhenNoHeartbeatSucceedsForTheLeaseDuration_ThenLeadershipAndDaemonEndExactlyThen(
        int wallClockStepMinutes)
    {
        // Arrange: clock synchronization can move the wall clock back at any time, including during a heartbeat
        var agent = new ControllableDaemonAgent(DaemonId);
        mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => time.StepWallClock(TimeSpan.FromMinutes(wallClockStepMinutes)))
            .ReturnsAsync((HeartbeatRequest request, CancellationToken _) => new HeartbeatResult(true, request.NodeId, request.HeartbeatId, options.LeaseDuration));
        var worker = CreateWorker(agents: [agent]);
        SetupActiveAssignment(worker.NodeId);
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        var token = worker.LeadershipToken;

        // Act
        time.Advance(LessClockDrift(options.LeaseDuration) - TimeSpan.FromMilliseconds(1));

        // Assert
        Assert.True(worker.IsLeader);
        Assert.True(agent.IsRunning);

        // Act
        time.Advance(TimeSpan.FromMilliseconds(1));

        // Assert
        Assert.False(worker.IsLeader);
        Assert.True(token.IsCancellationRequested);
        Assert.False(agent.IsRunning);
    }

    [Fact]
    public async Task GivenNodeRunningADaemon_WhenItsHeartbeatsFail_ThenTheDaemonRunsUntilTheLeaseRunsOutAndTheNodeOffersNoDaemonUntilItHoldsALeaseAgain()
    {
        // Arrange: a heartbeat can fail on the node yet reach the database, so the leader sees the node alive;
        // once its lease ran out, the node declares no daemon so the leader moves them
        var agent = new ControllableDaemonAgent(DaemonId);
        SetupHeartbeat(isLeader: false);
        var worker = CreateWorker(agents: [agent]);
        SetupActiveAssignment(worker.NodeId);
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        List<IReadOnlyList<string>> declared = [];
        mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .Callback((HeartbeatRequest request, CancellationToken _) => declared.Add(request.DaemonIds))
            .ThrowsAsync(new TimeoutException("No response."));

        // Act
        time.Advance(options.HeartbeatInterval);
        await worker.PerformHeartbeatAsync(CancellationToken.None);

        // Assert: a failed heartbeat does not stop the daemon; the lease does, when it runs out
        Assert.True(agent.IsRunning);

        // Act
        time.Advance(options.HeldLease - options.HeartbeatInterval);
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .Callback((HeartbeatRequest request, CancellationToken _) => declared.Add(request.DaemonIds))
            .ReturnsAsync((HeartbeatRequest request, CancellationToken _) => new HeartbeatResult(false, Guid.NewGuid(), Guid.NewGuid(), options.LeaseDuration));
        await worker.PerformHeartbeatAsync(CancellationToken.None);

        // Assert
        Assert.False(agent.IsRunning);

        // Act
        await worker.PerformHeartbeatAsync(CancellationToken.None);

        // Assert
        Assert.Equal([[DaemonId], [], [], [DaemonId]], declared);
        Assert.True(agent.IsRunning);
    }

    [Fact]
    public async Task GivenNodeThatCannotReadItsAssignments_WhenItsHeartbeatsSucceed_ThenItsDaemonStopsWhenTheLeaseOfTheLastHeartbeatThatReadThemRunsOut()
    {
        // Arrange: a node that does not read its assignments would miss a revocation, so the leader could only
        // move the daemon while it still runs here
        var agent = new ControllableDaemonAgent(DaemonId);
        SetupHeartbeat(isLeader: false);
        var worker = CreateWorker(agents: [agent]);
        SetupActiveAssignment(worker.NodeId);
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        mockAssignmentPersistence
            .Setup(p => p.GetNodeAssignmentsAsync(ServiceName, worker.NodeId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("No response."));

        // Act
        for (var elapsed = options.HeartbeatInterval; elapsed < options.LeaseDuration; elapsed += options.HeartbeatInterval)
        {
            time.Advance(options.HeartbeatInterval);
            await worker.PerformHeartbeatAsync(CancellationToken.None);
        }

        // Assert
        Assert.True(agent.IsRunning);

        // Act
        time.Advance(options.HeartbeatInterval);
        await worker.PerformHeartbeatAsync(CancellationToken.None);

        // Assert
        Assert.False(agent.IsRunning);
    }

    [Fact]
    public async Task GivenCoordinationThatOutlastsTheHeartbeatInterval_WhenTheNextHeartbeatIsDue_ThenItStopsSoTheLeaseIsRenewedInTime()
    {
        // Arrange: every query finishes within the query timeout, but a slow database and several daemons to
        // place add up past the heartbeat interval, and every node would then lose its lease and fence its daemons
        var agent = new ControllableDaemonAgent(DaemonId);
        SetupHeartbeat(isLeader: true);
        var worker = CreateWorker(agents: [agent]);
        SetupActiveAssignment(worker.NodeId);
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        var queryTime = options.QueryTimeout - TimeSpan.FromMilliseconds(50);
        Task<T> SlowQuery<T>(T result, CancellationToken token)
        {
            time.Advance(queryTime);
            token.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }

        DaemonAssignment active = new(DaemonId, worker.NodeId, 1, DaemonAssignmentState.Active, DateTime.UtcNow);
        mockAssignmentPersistence
            .Setup(p => p.GetNodeAssignmentsAsync(ServiceName, worker.NodeId, It.IsAny<CancellationToken>()))
            .Returns((string _, Guid _, CancellationToken token) => SlowQuery<IReadOnlyList<DaemonAssignment>>([active], token));
        mockAssignmentPersistence
            .Setup(p => p.GetAllAssignmentsAsync(ServiceName, It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken token) => SlowQuery<IReadOnlyList<DaemonAssignment>>([active], token));
        mockPersistence
            .Setup(p => p.GetNodesAsync(ServiceName, It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken token) => SlowQuery<IReadOnlyList<NodeRegistration>>(
                [new NodeRegistration(worker.NodeId, [DaemonId, .. Enumerable.Range(0, 12).Select(i => $"d{i}")], Guid.NewGuid(), options.NodeRegistrationTtl)],
                token));
        mockAssignmentPersistence
            .Setup(p => p.AssignAsync(ServiceName, It.IsAny<string>(), worker.NodeId, null, It.IsAny<CancellationToken>()))
            .Returns((string _, string daemonId, Guid nodeId, DaemonAssignment? _, CancellationToken token) =>
                SlowQuery<DaemonAssignment?>(new DaemonAssignment(daemonId, nodeId, 1, DaemonAssignmentState.Assigning, DateTime.UtcNow), token));
        var heartbeatStart = time.GetTimestamp();

        // Act
        await worker.PerformHeartbeatAsync(CancellationToken.None);

        // Assert
        Assert.True(time.GetElapsedTime(heartbeatStart) < options.HeartbeatInterval + options.QueryTimeout);
        Assert.True(worker.IsLeader);
        Assert.True(agent.IsRunning);
        mockPersistence.Verify(
            p => p.ResignLeadershipAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GivenNodeIdUsedByAnEarlierProcess_WhenTheNewProcessFirstHeartbeats_ThenItGivesUpThatProcesssAssignmentsInsteadOfRunningThem()
    {
        // Arrange: a stable node id, such as one derived from a pod name, comes back after its process crashed. The
        // leader may be moving that process's daemons while the new process registers, so running them could run a
        // daemon on two nodes; a new process was given nothing before it registered.
        var agent = new ControllableDaemonAgent(DaemonId);
        SetupHeartbeat(isLeader: false);
        var worker = CreateWorker(agents: [agent]);
        SetupActiveAssignment(worker.NodeId);
        SetupReleaseRemovesAssignments(worker.NodeId);

        // Act
        await worker.PerformHeartbeatAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, agent.RunCount);
        mockAssignmentPersistence.Verify(p => p.RemoveAsync(ServiceName, DaemonId, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenNodeSuspendedLongerThanItsRegistrationTtl_WhenItResumes_ThenItStopsLeadingAndActingAtOnceAndGivesUpItsDaemons()
    {
        // Arrange: while a system is suspended its monotonic clock and timers stand still, but the leases in the
        // database run out and the leader moves the node's daemons
        var agent = new ControllableDaemonAgent(DaemonId);
        SetupHeartbeat(isLeader: true);
        var worker = CreateWorker(agents: [agent]);
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        SetupActiveAssignment(worker.NodeId);
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        SetupReleaseRemovesAssignments(worker.NodeId);

        // Act
        time.StepWallClock(options.NodeRegistrationTtl);

        // Assert
        Assert.False(worker.IsLeader);

        // Act
        time.Advance(options.QueryTimeout);

        // Assert
        Assert.False(agent.IsRunning);

        // Act
        await worker.PerformHeartbeatAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, agent.RunCount);
        mockAssignmentPersistence.Verify(p => p.RemoveAsync(ServiceName, DaemonId, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(false, -1)]
    public async Task GivenAnotherNodesLease_WhenThisNodeSawItUnrenewedForItsWholeDuration_ThenItAsksToTakeItOverAndNotBefore(
        bool renewedMeanwhile,
        int wallClockSteps)
    {
        // Arrange: the lease is watched from the moment its current heartbeat is first seen, on this node's own
        // monotonic clock, so neither a time the database records nor a step in this node's wall clock decides
        // when it can be taken over
        var holder = Guid.NewGuid();
        var lease = Guid.NewGuid();
        var leaseDuration = TimeSpan.FromSeconds(20);
        List<HeartbeatRequest> requests = [];
        mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .Callback((HeartbeatRequest request, CancellationToken _) => requests.Add(request))
            .ReturnsAsync(() => new HeartbeatResult(false, holder, lease, leaseDuration));
        var worker = CreateWorker();
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        time.StepWallClock(leaseDuration * wallClockSteps);

        // Act
        time.Advance(leaseDuration - TimeSpan.FromMilliseconds(1));
        if (renewedMeanwhile)
        {
            lease = Guid.NewGuid();
        }

        await worker.PerformHeartbeatAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMilliseconds(1));
        await worker.PerformHeartbeatAsync(CancellationToken.None);

        // Assert
        Assert.Null(requests[0].TakeOverLeaseHeartbeatId);
        Assert.Null(requests[1].TakeOverLeaseHeartbeatId);
        Assert.Equal(renewedMeanwhile ? null : lease, requests[2].TakeOverLeaseHeartbeatId);
    }

    // --- Registration that may have expired ---

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public async Task GivenNodeSilentAroundItsRegistrationTtl_WhenAHeartbeatSucceedsAgain_ThenItReleasesItsAssignmentsOnlyIfTheRegistrationMayHaveExpired(
        int millisecondsAfterTheRelease,
        bool releases)
    {
        // Arrange: once the registration may have expired, the leader may be moving the daemon to another node;
        // before that, the assignment is still this node's. The leader waits the TTL on its own clock, which can run
        // slower than this node's, so the node gives its daemons up a little earlier.
        var agent = new ControllableDaemonAgent(DaemonId);
        SetupHeartbeat(isLeader: false);
        var worker = CreateWorker(agents: [agent]);
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        SetupActiveAssignment(worker.NodeId);
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        SetupReleaseRemovesAssignments(worker.NodeId);

        // Act: once the lease ran out, the node declares its daemons, and so runs them, one heartbeat later
        time.Advance(LessClockDrift(options.NodeRegistrationTtl) + TimeSpan.FromMilliseconds(millisecondsAfterTheRelease));
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        await worker.PerformHeartbeatAsync(CancellationToken.None);

        // Assert
        Assert.Equal(releases ? 1 : 2, agent.RunCount);
        Assert.Equal(!releases, agent.IsRunning);
        mockAssignmentPersistence.Verify(
            p => p.RemoveAsync(ServiceName, DaemonId, 1, It.IsAny<CancellationToken>()),
            releases ? Times.Once() : Times.Never());
    }

    // --- Shutdown ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenNodeWhoseDaemonsStop_WhenShuttingDown_ThenItStopsThemGivesUpItsLeaseWithdrawsAndGivesUpItsDaemonsAndDeregisters(bool isLeader)
    {
        // Arrange: a follower too, since a heartbeat that failed after renewing the lease leaves it with a node that does
        // not lead. It withdraws its daemons, so the leader places no new one on it, and gives up its assignments before
        // it deregisters, so they move at once even if a late heartbeat registers it again.
        List<string> calls = [];
        var agent = new ControllableDaemonAgent(DaemonId);
        SetupHeartbeat(isLeader);
        var worker = CreateWorker(agents: [agent]);
        SetupActiveAssignment(worker.NodeId);
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        mockAssignmentPersistence
            .Setup(p => p.RemoveAsync(ServiceName, DaemonId, 1, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("release"))
            .ReturnsAsync(true);
        mockPersistence
            .Setup(p => p.DeregisterNodeAsync(worker.NodeId, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("deregister"))
            .Returns(Task.CompletedTask);

        var lastHeartbeat = sentHeartbeatIds.Last();

        // Act
        await worker.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.False(agent.IsRunning);
        Assert.Equal(["release", "deregister"], calls);
        mockPersistence.Verify(
            p => p.ResignLeadershipAsync(ServiceName, worker.NodeId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { lastHeartbeat })), It.IsAny<CancellationToken>()),
            Times.Once);
        mockPersistence.Verify(
            p => p.HeartbeatAsync(It.Is<HeartbeatRequest>(r => !r.ClaimsLease && r.DaemonIds.Count == 0), It.IsAny<CancellationToken>()),
            Times.Once);
        mockPersistence.Verify(p => p.DeregisterNodeAsync(worker.NodeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenLeaderWithADaemonSlowToStop_WhenShuttingDown_ThenItHandsOverLeadershipAtOnceGivesUpTheDaemonsThatStoppedAndStaysRegisteredWhileTheOtherRuns()
    {
        // Arrange: leadership does not depend on the daemons, but deregistering, or giving up the daemon still
        // running, would let the leader start it elsewhere while it still runs here
        var agent = new ControllableDaemonAgent(DaemonId) { IgnoresCancellation = true };
        var stopped = new ControllableDaemonAgent("stopped");
        var daemonOptions = new DaemonOptions { DrainTimeout = TimeSpan.FromSeconds(1) };
        SetupHeartbeat(isLeader: true);
        var worker = CreateWorker(agents: [agent, stopped], daemonOptions);
        DaemonAssignment[] assignments =
        [
            new(DaemonId, worker.NodeId, 1, DaemonAssignmentState.Active, DateTime.UtcNow),
            new("stopped", worker.NodeId, 1, DaemonAssignmentState.Active, DateTime.UtcNow),
        ];
        await worker.PerformHeartbeatAsync(CancellationToken.None);
        mockAssignmentPersistence.Setup(p => p.GetNodeAssignmentsAsync(ServiceName, worker.NodeId, It.IsAny<CancellationToken>())).ReturnsAsync(assignments);
        mockAssignmentPersistence.Setup(p => p.GetAllAssignmentsAsync(ServiceName, It.IsAny<CancellationToken>())).ReturnsAsync(assignments);
        await worker.PerformHeartbeatAsync(CancellationToken.None);

        var lastHeartbeat = sentHeartbeatIds.Last();

        // Act
        var shutdown = worker.ShutdownAsync();

        // Assert
        Assert.False(worker.IsLeader);
        mockPersistence.Verify(
            p => p.ResignLeadershipAsync(ServiceName, worker.NodeId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { lastHeartbeat })), It.IsAny<CancellationToken>()),
            Times.Once);

        // Act
        time.Advance(daemonOptions.DrainTimeout);
        await shutdown.WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        mockAssignmentPersistence.Verify(p => p.RemoveAsync(ServiceName, "stopped", 1, It.IsAny<CancellationToken>()), Times.Once);
        mockPersistence.Verify(p => p.DeregisterNodeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenHostStoppingDuringAHeartbeat_WhenItStops_ThenTheHeartbeatEndsBeforeTheNodeDeregisters(bool heartbeatReturns)
    {
        // Arrange: a heartbeat the node gave up on could still reach the database after the node deregistered, and
        // register it again; one that never returns ends at the query timeout
        List<string> calls = [];
        var heartbeatStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var heartbeatReturned = new TaskCompletionSource<HeartbeatResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken heartbeatToken = default;
        mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (HeartbeatRequest request, CancellationToken token) =>
            {
                if (!request.ClaimsLease)
                {
                    calls.Add("withdraw");
                    return new HeartbeatResult(false, Guid.Empty, Guid.Empty, TimeSpan.Zero);
                }

                heartbeatToken = token;
                heartbeatStarted.TrySetResult();
                var result = await heartbeatReturned.Task.WaitAsync(token);
                calls.Add("heartbeat");
                return result;
            });
        mockPersistence.Setup(p => p.DeregisterNodeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("deregister"))
            .Returns(Task.CompletedTask);
        var worker = CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        await heartbeatStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // Act
        var stopping = worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.False(heartbeatToken.IsCancellationRequested);
        Assert.Empty(calls);

        // Act
        if (heartbeatReturns)
        {
            heartbeatReturned.SetResult(new HeartbeatResult(false, Guid.NewGuid(), Guid.NewGuid(), options.LeaseDuration));
        }
        else
        {
            time.Advance(options.HeartbeatTimeout);
        }

        await stopping.WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.Equal(heartbeatReturns ? ["heartbeat", "withdraw", "deregister"] : ["withdraw", "deregister"], calls);
    }

    [Theory]
    [InlineData("   ", 1, false)]
    [InlineData("s", ValidationConstants.MaxServiceNameLength + 1, false)]
    [InlineData("service", 1, true)]
    public void GivenNodeIdentityWithoutAUsableServiceNameOrNodeId_WhenCreatingTheWorker_ThenItFailsAtOnce(string servicePart, int repeat, bool emptyNodeId)
    {
        // Arrange: a replaced identity is not covered by the options validation. Every heartbeat would fail without
        // a usable service name, and an empty node id, as an identity bound to a missing setting gives, is how
        // leader election and placement say "no node".
        var serviceName = string.Concat(Enumerable.Repeat(servicePart, repeat));
        var nodeId = emptyNodeId ? Guid.Empty : Guid.NewGuid();
        var identity = Mock.Of<INodeIdentity>(n => n.NodeId == nodeId && n.ServiceName == serviceName);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => CreateWorker(nodeIdentity: identity));
    }

    /// <summary>
    /// Returns how long the node itself counts <paramref name="duration"/>, which other nodes count on clocks that
    /// can run slower than its own.
    /// </summary>
    private static TimeSpan LessClockDrift(TimeSpan duration) => duration - (duration * ValidationConstants.MaxClockDriftRate);

    private void SetupHeartbeat(bool isLeader)
    {
        mockPersistence.Setup(p => p.HeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .Callback((HeartbeatRequest request, CancellationToken _) => sentHeartbeatIds.Add(request.HeartbeatId))
            .ReturnsAsync((HeartbeatRequest request, CancellationToken _) =>
                new HeartbeatResult(
                    isLeader,
                    isLeader ? request.NodeId : Guid.NewGuid(),
                    isLeader ? request.HeartbeatId : Guid.NewGuid(),
                    options.LeaseDuration));
    }

    private void SetupActiveAssignment(Guid nodeId)
    {
        DaemonAssignment[] assignments = [new DaemonAssignment(DaemonId, nodeId, 1, DaemonAssignmentState.Active, DateTime.UtcNow)];
        mockAssignmentPersistence
            .Setup(p => p.GetNodeAssignmentsAsync(ServiceName, nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignments);
        mockAssignmentPersistence
            .Setup(p => p.GetAllAssignmentsAsync(ServiceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignments);
        mockPersistence
            .Setup(p => p.GetNodesAsync(ServiceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [new NodeRegistration(nodeId, [DaemonId], Guid.NewGuid(), options.NodeRegistrationTtl)]);
    }

    private void SetupReleaseRemovesAssignments(Guid nodeId)
    {
        mockAssignmentPersistence
            .Setup(p => p.RemoveAsync(ServiceName, It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback(() => mockAssignmentPersistence
                .Setup(p => p.GetNodeAssignmentsAsync(ServiceName, nodeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync([]))
            .ReturnsAsync(true);
    }

    private HeartbeatWorker CreateWorker(
        IEnumerable<IDaemonAgent>? agents = null,
        DaemonOptions? daemonOptions = null,
        INodeIdentity? nodeIdentity = null,
        TaskScheduler? callbackScheduler = null)
    {
        callbackScheduler ??= InlineTaskScheduler.Instance;
        nodeIdentity ??= Mock.Of<INodeIdentity>(n => n.NodeId == Guid.NewGuid() && n.ServiceName == ServiceName);

        var daemonCoordinator = TestNodes.CreateCoordinator(
            mockAssignmentPersistence.Object,
            mockPersistence.Object,
            agents ?? [],
            daemonOptions ?? new DaemonOptions { DrainTimeout = TimeSpan.FromMilliseconds(1500) },
            options,
            nodeIdentity,
            time,
            callbackScheduler,
            leaderElectionObservers: [mockObserver.Object]);

        return TestNodes.CreateWorker(mockPersistence.Object, daemonCoordinator, options, nodeIdentity, time, callbackScheduler, [mockObserver.Object]);
    }
}

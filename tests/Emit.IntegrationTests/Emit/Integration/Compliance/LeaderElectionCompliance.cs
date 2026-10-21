namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions.LeaderElection;
using Xunit;
using static Emit.IntegrationTests.Integration.TestHelpers;

/// <summary>
/// Compliance tests for leader election persistence. Derived classes provide a database-specific
/// <see cref="ILeaderElectionPersistence"/> instance.
/// </summary>
[Trait("Category", "Integration")]
public abstract class LeaderElectionCompliance : IAsyncLifetime
{
    /// <summary>
    /// Instance identifier every heartbeat sends. It contains quotes, operators and non-ASCII characters, so
    /// every test also proves the providers never read it as an expression or a field path.
    /// </summary>
    private const string InstanceId = "it's \"quoted\" $set: { $where: 1 } \\ ' é ✓";

    /// <summary>
    /// Lease every heartbeat requests. No test waits for it to run out: a lease is taken over by naming the
    /// heartbeat recorded on it.
    /// </summary>
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Registration TTL every heartbeat requests, longer than the lease as the options require.
    /// </summary>
    private static readonly TimeSpan NodeRegistrationTtl = TimeSpan.FromSeconds(12);

    public enum LeaseHolder
    {
        ThisNode,
        AnotherNode,
        Nobody,
    }

    public enum TakeOver
    {
        None,
        OfTheRecordedHeartbeat,
        OfAHeartbeatRenewedSince,
    }

    public enum Resign
    {
        NamingTheRenewingHeartbeat,
        NamingOnlyAnEarlierHeartbeat,
        UnderAnotherServiceName,
    }

    /// <summary>
    /// Gets the persistence implementation under test.
    /// </summary>
    protected abstract ILeaderElectionPersistence Persistence { get; }

    /// <inheritdoc/>
    public abstract Task InitializeAsync();

    /// <inheritdoc/>
    public abstract Task DisposeAsync();

    // --- Heartbeats that do not claim the lease ---

    [Theory]
    [InlineData(LeaseHolder.ThisNode)]
    [InlineData(LeaseHolder.AnotherNode)]
    [InlineData(LeaseHolder.Nobody)]
    public async Task GivenHeartbeatThatDoesNotClaimTheLease_WhenSent_ThenItRegistersTheNodeWithoutTakingOrRenewingTheLeaseAndReportsWhoHoldsIt(LeaseHolder leaseHolder)
    {
        // Arrange: a node whose last heartbeat failed may have written the lease without knowing it, and renewing
        // it again would keep restarting every other node's wait to take it over; a node leaving its service sends
        // one right after giving its lease up. A node waits out the lease it is told of, for the duration its holder
        // recorded, before taking it over.
        var service = NewServiceName();
        var node = Guid.NewGuid();
        var holder = leaseHolder == LeaseHolder.ThisNode ? node : Guid.NewGuid();
        var claim = Request(service, holder) with { LeaseDuration = TimeSpan.FromSeconds(42) };
        if (leaseHolder != LeaseHolder.Nobody)
        {
            Assert.True((await Persistence.HeartbeatAsync(claim)).IsLeader);
        }

        var request = Request(service, node) with { ClaimsLease = false };

        // Act
        var result = await Persistence.HeartbeatAsync(request);

        // Assert
        Assert.Equal(
            leaseHolder == LeaseHolder.Nobody ? (false, Guid.Empty, Guid.Empty, TimeSpan.Zero) : (false, holder, claim.HeartbeatId, claim.LeaseDuration),
            (result.IsLeader, result.LeaderNodeId, result.LeaseHeartbeatId, result.LeaseDuration));
        Assert.Equal(request.HeartbeatId, Assert.Single(await Persistence.GetNodesAsync(service), n => n.NodeId == node).HeartbeatId);
        Assert.Equal(leaseHolder == LeaseHolder.Nobody, (await Persistence.HeartbeatAsync(Request(service, Guid.NewGuid()))).IsLeader);
    }

    // --- Lease takeover ---

    [Theory]
    [InlineData(TakeOver.None, false)]
    [InlineData(TakeOver.OfTheRecordedHeartbeat, true)]
    [InlineData(TakeOver.OfAHeartbeatRenewedSince, false)]
    public async Task GivenAnotherNodesLease_WhenANodeAsksToTakeItOver_ThenItGetsItOnlyIfTheHeartbeatItSawGoUnrenewedIsStillRecorded(
        TakeOver takeOver,
        bool obtains)
    {
        // Arrange: the node judged on its own clock that the lease went unrenewed; the database only checks that
        // the lease was not renewed since, so no time it records decides the takeover
        var service = NewServiceName();
        var holder = Guid.NewGuid();
        var node = Guid.NewGuid();
        var first = Request(service, holder);
        Assert.True((await Persistence.HeartbeatAsync(first)).IsLeader);
        if (takeOver == TakeOver.OfAHeartbeatRenewedSince)
        {
            Assert.True((await Persistence.HeartbeatAsync(Request(service, holder))).IsLeader);
        }

        // Act
        var result = await Persistence.HeartbeatAsync(Request(service, node) with
        {
            TakeOverLeaseHeartbeatId = takeOver == TakeOver.None ? null : first.HeartbeatId,
        });

        // Assert
        Assert.Equal(obtains, result.IsLeader);
        Assert.Equal(obtains ? node : holder, result.LeaderNodeId);
        Assert.Equal(obtains, (await Persistence.HeartbeatAsync(Request(service, holder))).LeaderNodeId == node);
    }

    // --- Services sharing a database ---

    [Fact]
    public async Task GivenNodesOfTwoServices_WhenGettingTheirRegistrations_ThenReturnsOnlyTheServicesNodesAsTheyLastHeartbeated()
    {
        // Arrange: a node declares a new daemon on a later heartbeat, as after a rolling deployment
        var serviceA = NewServiceName();
        var serviceB = NewServiceName();
        var nodeA1 = Guid.NewGuid();
        var nodeA2 = Guid.NewGuid();
        await Persistence.HeartbeatAsync(Request(serviceA, nodeA1, ["outbox"]));
        var latest = Request(serviceA, nodeA1, ["outbox", "search-index"]) with { NodeTtl = TimeSpan.FromSeconds(42) };
        await Persistence.HeartbeatAsync(latest);
        await Persistence.HeartbeatAsync(Request(serviceA, nodeA2, []));
        await Persistence.HeartbeatAsync(Request(serviceB, Guid.NewGuid(), ["outbox"]));

        // Act
        var nodes = await Persistence.GetNodesAsync(serviceA);

        // Assert
        Assert.Equal(2, nodes.Count);
        var a1 = nodes.Single(n => n.NodeId == nodeA1);
        Assert.Equal(["outbox", "search-index"], a1.DaemonIds.Order());
        Assert.Equal((latest.HeartbeatId, latest.NodeTtl), (a1.HeartbeatId, a1.RegistrationTtl));
        Assert.Empty(nodes.Single(n => n.NodeId == nodeA2).DaemonIds);
    }

    [Fact]
    public async Task GivenRemovalThatIsCancelled_WhenRemovingRegistrations_ThenItReportsOnlyWhatItRemovedWithoutFailing()
    {
        // Arrange: the leader moves the daemons of the nodes reported removed, and tries the others again later
        var service = NewServiceName();
        var node = Guid.NewGuid();
        await Persistence.HeartbeatAsync(Request(service, node));
        var registration = Assert.Single(await Persistence.GetNodesAsync(service));

        // Act
        var removed = await Persistence.RemoveNodesAsync(service, [registration], new CancellationToken(canceled: true));

        // Assert
        var remaining = await Persistence.GetNodesAsync(service);
        Assert.Equal(removed.Contains(node), !remaining.Any(n => n.NodeId == node));
    }

    [Fact]
    public async Task GivenRegistrationsAsTheLeaderRead_WhenRemovingThem_ThenOnlyThoseOfTheServiceNotRenewedSinceAreRemoved()
    {
        // Arrange: the leader read the registrations, then judged them dead on its own clock
        var service = NewServiceName();
        var otherService = NewServiceName();
        var silent = Guid.NewGuid();
        var renewed = Guid.NewGuid();
        var otherServicesNode = Guid.NewGuid();
        await Persistence.HeartbeatAsync(Request(service, silent));
        await Persistence.HeartbeatAsync(Request(service, renewed));
        await Persistence.HeartbeatAsync(Request(otherService, otherServicesNode));
        var read = (await Persistence.GetNodesAsync(service)).Concat(await Persistence.GetNodesAsync(otherService)).ToList();
        await Persistence.HeartbeatAsync(Request(service, renewed));

        // Act
        var removed = await Persistence.RemoveNodesAsync(service, read);

        // Assert
        Assert.Equal([silent], removed);
        Assert.Equal([renewed], (await Persistence.GetNodesAsync(service)).Select(n => n.NodeId));
        Assert.Equal([otherServicesNode], (await Persistence.GetNodesAsync(otherService)).Select(n => n.NodeId));
    }

    [Theory]
    [InlineData(Resign.NamingTheRenewingHeartbeat, true)]
    [InlineData(Resign.NamingOnlyAnEarlierHeartbeat, false)]
    [InlineData(Resign.UnderAnotherServiceName, false)]
    public async Task GivenLeadersOfTwoServices_WhenOneResigns_ThenItsLeaseIsFreedAtOnceOnlyByAResignForItsServiceNamingTheHeartbeatThatRenewedIt(
        Resign resign,
        bool freed)
    {
        // Arrange: each service elects its own leader; a resign naming only earlier heartbeats is one that reached
        // the database after the leader renewed its lease
        var service = NewServiceName();
        var otherService = NewServiceName();
        var leader = Guid.NewGuid();
        var first = Request(service, leader);
        var renewal = Request(service, leader);
        Assert.True((await Persistence.HeartbeatAsync(first)).IsLeader);
        Assert.Equal(renewal.HeartbeatId, (await Persistence.HeartbeatAsync(renewal)).LeaseHeartbeatId);
        Assert.True((await Persistence.HeartbeatAsync(Request(otherService, Guid.NewGuid()))).IsLeader);

        // Act
        await (resign switch
        {
            Resign.NamingTheRenewingHeartbeat => Persistence.ResignLeadershipAsync(service, leader, [first.HeartbeatId, renewal.HeartbeatId]),
            Resign.NamingOnlyAnEarlierHeartbeat => Persistence.ResignLeadershipAsync(service, leader, [first.HeartbeatId]),
            _ => Persistence.ResignLeadershipAsync(otherService, leader, [first.HeartbeatId, renewal.HeartbeatId]),
        });

        // Assert
        Assert.Equal(freed, (await Persistence.HeartbeatAsync(Request(service, Guid.NewGuid()))).IsLeader);
    }

    [Fact]
    public async Task GivenNodesOfTwoServices_WhenOneNodeAndAnUnknownOneDeregister_ThenOnlyThatNodesRegistrationIsRemoved()
    {
        // Arrange
        var service = NewServiceName();
        var otherService = NewServiceName();
        var (leaving, staying, otherServicesNode) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await Persistence.HeartbeatAsync(Request(service, leaving));
        await Persistence.HeartbeatAsync(Request(service, staying));
        await Persistence.HeartbeatAsync(Request(otherService, otherServicesNode));

        // Act
        await Persistence.DeregisterNodeAsync(leaving);
        await Persistence.DeregisterNodeAsync(Guid.NewGuid());

        // Assert
        Assert.Equal([staying], (await Persistence.GetNodesAsync(service)).Select(n => n.NodeId));
        Assert.Equal([otherServicesNode], (await Persistence.GetNodesAsync(otherService)).Select(n => n.NodeId));
    }

    [Fact]
    public async Task GivenManyNodesOfOneService_WhenTheyHeartbeatOrTakeOverTheSameLeaseConcurrently_ThenExactlyOneLeadsAndAllAgreeOnIt()
    {
        for (var round = 0; round < 5; round++)
        {
            // Arrange
            var service = NewServiceName();
            var nodes = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();

            // Act: first nobody holds the lease, then all the others saw it go unrenewed; the holder would renew it
            // whatever the others do, so it stays out of the race
            var acquired = await Task.WhenAll(nodes.Select(n => Task.Run(() => Persistence.HeartbeatAsync(Request(service, n)))));
            var unrenewed = acquired[0].LeaseHeartbeatId;
            var holder = acquired[0].LeaderNodeId;
            var takenOver = await Task.WhenAll(nodes.Where(n => n != holder).Select(n => Task.Run(() =>
                Persistence.HeartbeatAsync(Request(service, n) with { TakeOverLeaseHeartbeatId = unrenewed }))));

            // Assert
            foreach (var results in new[] { acquired, takenOver })
            {
                var leader = Assert.Single(results, r => r.IsLeader).LeaderNodeId;
                Assert.All(results, r => Assert.Equal(leader, r.LeaderNodeId));
            }
        }
    }

    // --- Helpers ---

    protected static HeartbeatRequest Request(string serviceName, Guid nodeId, IReadOnlyList<string>? daemonIds = null) =>
        new(serviceName, nodeId, InstanceId, daemonIds ?? [], LeaseDuration, NodeRegistrationTtl, Guid.NewGuid());

}

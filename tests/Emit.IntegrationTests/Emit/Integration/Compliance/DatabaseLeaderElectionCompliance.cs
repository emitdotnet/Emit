namespace Emit.IntegrationTests.Integration.Compliance;

using Xunit;
using static Emit.IntegrationTests.Integration.TestHelpers;

/// <summary>
/// Leader election compliance tests that need a real database, whose writes can wait on locks held elsewhere.
/// </summary>
[Trait("Category", "Integration")]
public abstract class DatabaseLeaderElectionCompliance : LeaderElectionCompliance
{
    /// <summary>
    /// Stalls this test's lease writes until disposed, as a contended or degraded database does.
    /// </summary>
    protected abstract Task<IAsyncDisposable> BlockLeaseAsync(string serviceName);

    [Fact]
    public async Task GivenHeartbeatThatCannotComplete_WhenItTimesOut_ThenTheNodeIsNotRegistered()
    {
        // Arrange: a node whose heartbeats fail stops its daemons, so it must not look alive to the leader
        var service = NewServiceName();
        await Persistence.HeartbeatAsync(Request(service, Guid.NewGuid()));
        var node = Guid.NewGuid();

        // Act
        await using (await BlockLeaseAsync(service))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Persistence.HeartbeatAsync(Request(service, node), timeout.Token));
        }

        // Assert
        Assert.DoesNotContain(await Persistence.GetNodesAsync(service), n => n.NodeId == node);
    }
}

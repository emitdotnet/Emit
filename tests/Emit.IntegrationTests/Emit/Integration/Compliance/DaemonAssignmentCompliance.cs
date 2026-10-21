namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions.Daemon;
using Xunit;
using static Emit.IntegrationTests.Integration.TestHelpers;

/// <summary>
/// Compliance tests for daemon assignment persistence. Derived classes provide a database-specific
/// <see cref="IDaemonAssignmentPersistence"/> instance.
/// </summary>
[Trait("Category", "Integration")]
public abstract class DaemonAssignmentCompliance : IAsyncLifetime
{
    private const string DaemonId = "test:daemon";
    private const string DaemonId2 = "test:daemon-2";
    private const string SharedDaemonId = "emit:outbox";

    public enum Write
    {
        Assign,
        Acknowledge,
        Revoke,
        ConfirmDrain,
        Remove,
    }

    public enum Release
    {
        Removed,
        DrainedOnceActive,
        DrainedBeforeAcknowledged,
    }

    /// <summary>
    /// Gets the persistence implementation under test.
    /// </summary>
    protected abstract IDaemonAssignmentPersistence Persistence { get; }

    private readonly Guid nodeA = Guid.NewGuid();
    private readonly Guid nodeB = Guid.NewGuid();
    private readonly string serviceName = NewServiceName();
    private readonly string serviceA = NewServiceName();
    private readonly string serviceB = NewServiceName();

    /// <inheritdoc/>
    public abstract Task InitializeAsync();

    /// <inheritdoc/>
    public abstract Task DisposeAsync();

    // --- Assignment ---

    [Theory]
    [InlineData(DaemonAssignmentState.Assigning)]
    [InlineData(DaemonAssignmentState.Active)]
    [InlineData(DaemonAssignmentState.Revoking)]
    public async Task GivenExistingAssignment_WhenReassignedFromItAsRead_ThenTheNextGenerationIsAssigned(DaemonAssignmentState readState)
    {
        // Arrange: the leader reassigns an unacknowledged daemon, or a running or revoked one whose node is gone
        var read = await Persistence.AssignAsync(serviceName, DaemonId, nodeA, expected: null, CancellationToken.None);
        Assert.NotNull(read);
        if (readState != DaemonAssignmentState.Assigning)
        {
            Assert.True(await Persistence.AcknowledgeAsync(serviceName, DaemonId, read.Generation, CancellationToken.None));
            read = readState == DaemonAssignmentState.Active
                ? Assert.Single(await Persistence.GetAllAssignmentsAsync(serviceName, CancellationToken.None))
                : await Persistence.RevokeAsync(serviceName, DaemonId, read.Generation, CancellationToken.None);
            Assert.NotNull(read);
            Assert.Equal(readState, read.State);
        }

        // Act
        var reassigned = await Persistence.AssignAsync(serviceName, DaemonId, nodeB, read, CancellationToken.None);

        // Assert
        Assert.NotNull(reassigned);
        Assert.Equal(read.Generation + 1, reassigned.Generation);
        Assert.Equal(nodeB, reassigned.AssignedNodeId);
        Assert.NotEqual(Guid.Empty, reassigned.FencingKey);
        Assert.Equal(read.FencingKey, reassigned.FencingKey);
        Assert.Equal(DaemonAssignmentState.Assigning, reassigned.State);
    }

    [Fact]
    public async Task GivenAssignmentsOnTwoNodes_WhenRead_ThenTheServiceSeesBothAndEachNodeOnlyItsOwnInItsServiceUnderItsServicesFencingKey()
    {
        // Arrange: a node id can come back in another service, for example a stable id after the service was
        // renamed, and a row its earlier process left there must not be run as one of its own. A daemon's fencing
        // key is its own in each service, so a run in one service never fences a run in another.
        var assigned = await Persistence.AssignAsync(serviceName, DaemonId, nodeA, expected: null, CancellationToken.None);
        await Persistence.AssignAsync(serviceName, DaemonId2, nodeB, expected: null, CancellationToken.None);
        var inOtherService = await Persistence.AssignAsync(serviceA, DaemonId, nodeA, expected: null, CancellationToken.None);

        // Act
        var all = await Persistence.GetAllAssignmentsAsync(serviceName, CancellationToken.None);
        var onNodeA = await Persistence.GetNodeAssignmentsAsync(serviceName, nodeA, CancellationToken.None);

        // Assert
        Assert.Equal([DaemonId, DaemonId2], all.Select(a => a.DaemonId).Order());
        Assert.Equal((DaemonId, assigned!.FencingKey), (Assert.Single(onNodeA).DaemonId, Assert.Single(onNodeA).FencingKey));
        Assert.NotEqual(assigned.FencingKey, inOtherService!.FencingKey);
    }

    // --- State transitions ---

    [Theory]
    [InlineData(DaemonAssignmentState.Active, Write.Acknowledge)]
    [InlineData(DaemonAssignmentState.Active, Write.ConfirmDrain)]
    [InlineData(DaemonAssignmentState.Assigning, Write.ConfirmDrain)]
    [InlineData(DaemonAssignmentState.Revoking, Write.Acknowledge)]
    public async Task GivenAssignmentInAState_WhenAWriteOfItsGenerationThatStateDoesNotAllow_ThenItIsRefusedAndNothingChanges(
        DaemonAssignmentState state,
        Write write)
    {
        // Arrange: only an assignment being assigned can be acknowledged, and only a revoked one confirmed drained
        var assignment = await Persistence.AssignAsync(serviceName, DaemonId, nodeA, expected: null, CancellationToken.None);
        Assert.NotNull(assignment);
        if (state != DaemonAssignmentState.Assigning)
        {
            Assert.True(await Persistence.AcknowledgeAsync(serviceName, DaemonId, assignment.Generation, CancellationToken.None));
        }

        if (state == DaemonAssignmentState.Revoking)
        {
            assignment = await Persistence.RevokeAsync(serviceName, DaemonId, assignment.Generation, CancellationToken.None);
            Assert.NotNull(assignment);
        }

        // Act
        var applied = write == Write.Acknowledge
            ? await Persistence.AcknowledgeAsync(serviceName, DaemonId, assignment.Generation, CancellationToken.None)
            : await Persistence.ConfirmDrainAsync(serviceName, DaemonId, assignment.Generation, CancellationToken.None);

        // Assert
        Assert.False(applied);
        var unchanged = Assert.Single(await Persistence.GetAllAssignmentsAsync(serviceName, CancellationToken.None));
        Assert.Equal((assignment.Generation, state), (unchanged.Generation, unchanged.State));
    }

    // --- Revoke ---

    [Fact]
    public async Task GivenDaemonNeverAssigned_WhenRevoked_ThenNothingIsRevokedNorCreated()
    {
        // Act
        var result = await Persistence.RevokeAsync(serviceName, DaemonId, expectedGeneration: 1, CancellationToken.None);

        // Assert
        Assert.Null(result);
        Assert.Empty(await Persistence.GetAllAssignmentsAsync(serviceName, CancellationToken.None));
    }

    // --- Services sharing a database ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenSameDaemonIdInTwoServices_WhenOneServiceReleasesIt_ThenTheOtherServicesAssignmentIsUntouched(bool releasedByDrain)
    {
        // Arrange
        var inA = await Persistence.AssignAsync(serviceA, SharedDaemonId, nodeA, expected: null, CancellationToken.None);
        Assert.NotNull(inA);
        var inB = await Persistence.AssignAsync(serviceB, SharedDaemonId, nodeB, expected: null, CancellationToken.None);
        Assert.NotNull(inB);
        Assert.Equal((1L, 1L), (inA.Generation, inB.Generation));

        // Act
        Assert.True(await Persistence.AcknowledgeAsync(serviceA, SharedDaemonId, inA.Generation, CancellationToken.None));
        if (releasedByDrain)
        {
            var revoked = await Persistence.RevokeAsync(serviceA, SharedDaemonId, inA.Generation, CancellationToken.None);
            Assert.True(await Persistence.ConfirmDrainAsync(serviceA, SharedDaemonId, revoked!.Generation, CancellationToken.None));
        }
        else
        {
            Assert.True(await Persistence.RemoveAsync(serviceA, SharedDaemonId, inA.Generation, CancellationToken.None));
        }

        // Assert
        Assert.Empty(await Persistence.GetAllAssignmentsAsync(serviceA, CancellationToken.None));
        var remaining = Assert.Single(await Persistence.GetAllAssignmentsAsync(serviceB, CancellationToken.None));
        Assert.Equal((nodeB, 1L, DaemonAssignmentState.Assigning), (remaining.AssignedNodeId, remaining.Generation, remaining.State));
    }

    // --- Compare-and-swap writes ---

    [Fact]
    public async Task GivenAssignmentAcknowledgedSinceItWasRead_WhenReassignedFromThatRead_ThenNothingChanges()
    {
        // Arrange: the acknowledgement keeps the generation, so only the state tells the read is stale; reassigning
        // it would move a daemon its node has started
        var read = await Persistence.AssignAsync(serviceName, DaemonId, nodeA, expected: null, CancellationToken.None);
        Assert.NotNull(read);
        Assert.True(await Persistence.AcknowledgeAsync(serviceName, DaemonId, read.Generation, CancellationToken.None));

        // Act
        var assigned = await Persistence.AssignAsync(serviceName, DaemonId, nodeB, read, CancellationToken.None);

        // Assert
        Assert.Null(assigned);
        var unchanged = Assert.Single(await Persistence.GetAllAssignmentsAsync(serviceName, CancellationToken.None));
        Assert.Equal((nodeA, read.Generation, DaemonAssignmentState.Active), (unchanged.AssignedNodeId, unchanged.Generation, unchanged.State));
    }

    [Theory]
    [InlineData(Write.Assign)]
    [InlineData(Write.Acknowledge)]
    [InlineData(Write.Revoke)]
    [InlineData(Write.ConfirmDrain)]
    [InlineData(Write.Remove)]
    public async Task GivenAssignmentThatMovedPastAGeneration_WhenAWriteUsesThatGeneration_ThenItIsRefusedAndNothingChanges(Write write)
    {
        // Arrange: the revoke moved the assignment to the next generation, so writes based on the earlier read
        // come from a node or leader that no longer owns it
        var stale = await Persistence.AssignAsync(serviceName, DaemonId, nodeA, expected: null, CancellationToken.None);
        Assert.NotNull(stale);
        Assert.True(await Persistence.AcknowledgeAsync(serviceName, DaemonId, stale.Generation, CancellationToken.None));
        var current = await Persistence.RevokeAsync(serviceName, DaemonId, stale.Generation, CancellationToken.None);
        Assert.NotNull(current);

        // Act
        var applied = write switch
        {
            Write.Assign => await Persistence.AssignAsync(serviceName, DaemonId, nodeB, stale, CancellationToken.None) is not null,
            Write.Acknowledge => await Persistence.AcknowledgeAsync(serviceName, DaemonId, stale.Generation, CancellationToken.None),
            Write.Revoke => await Persistence.RevokeAsync(serviceName, DaemonId, stale.Generation, CancellationToken.None) is not null,
            Write.ConfirmDrain => await Persistence.ConfirmDrainAsync(serviceName, DaemonId, stale.Generation, CancellationToken.None),
            _ => await Persistence.RemoveAsync(serviceName, DaemonId, stale.Generation, CancellationToken.None),
        };

        // Assert
        Assert.False(applied);
        var unchanged = Assert.Single(await Persistence.GetAllAssignmentsAsync(serviceName, CancellationToken.None));
        Assert.Equal(
            (nodeA, current.Generation, DaemonAssignmentState.Revoking),
            (unchanged.AssignedNodeId, unchanged.Generation, unchanged.State));
    }

    [Theory]
    [InlineData(Release.Removed)]
    [InlineData(Release.DrainedOnceActive)]
    [InlineData(Release.DrainedBeforeAcknowledged)]
    public async Task GivenReleasedAssignment_WhenTheDaemonIsAssignedAgain_ThenItsGenerationsNeverRepeatAndItKeepsItsFencingKey(Release release)
    {
        // Arrange: a generation is a fencing token, so it must never be handed out twice, whichever way it was
        // released; the released row that remembers it is not an assignment of any node. The leader also revokes
        // an assignment its node has not acknowledged yet.
        var first = await Persistence.AssignAsync(serviceName, DaemonId, nodeA, expected: null, CancellationToken.None);
        Assert.NotNull(first);
        var lastGeneration = first.Generation;
        if (release != Release.Removed)
        {
            if (release == Release.DrainedOnceActive)
            {
                Assert.True(await Persistence.AcknowledgeAsync(serviceName, DaemonId, first.Generation, CancellationToken.None));
            }

            var revoked = await Persistence.RevokeAsync(serviceName, DaemonId, first.Generation, CancellationToken.None);
            Assert.NotNull(revoked);
            lastGeneration = revoked.Generation;
            Assert.True(await Persistence.ConfirmDrainAsync(serviceName, DaemonId, revoked.Generation, CancellationToken.None));
        }
        else
        {
            Assert.True(await Persistence.RemoveAsync(serviceName, DaemonId, first.Generation, CancellationToken.None));
        }

        Assert.Empty(await Persistence.GetAllAssignmentsAsync(serviceName, CancellationToken.None));
        Assert.Empty(await Persistence.GetNodeAssignmentsAsync(serviceName, nodeA, CancellationToken.None));

        // Act
        var again = await Persistence.AssignAsync(serviceName, DaemonId, nodeB, expected: null, CancellationToken.None);

        // Assert
        Assert.NotNull(again);
        Assert.True(again.Generation > lastGeneration);
        Assert.Equal(first.FencingKey, again.FencingKey);
    }

    [Fact]
    public async Task GivenManyLeadersAssigningTheSameNewDaemonAtOnce_WhenRacing_ThenExactlyOneSucceeds()
    {
        for (var round = 0; round < 5; round++)
        {
            // Arrange
            var daemonId = $"race-{round}";

            // Act
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
                Task.Run(() => Persistence.AssignAsync(serviceName, daemonId, Guid.NewGuid(), expected: null, CancellationToken.None))));

            // Assert
            var winner = Assert.Single(results, r => r is not null)!;
            var stored = Assert.Single(await Persistence.GetAllAssignmentsAsync(serviceName, CancellationToken.None), a => a.DaemonId == daemonId);
            Assert.Equal((winner.AssignedNodeId, 1L), (stored.AssignedNodeId, stored.Generation));
        }
    }
}

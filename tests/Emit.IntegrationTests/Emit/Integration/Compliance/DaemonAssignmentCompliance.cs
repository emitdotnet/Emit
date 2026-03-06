namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions.Daemon;
using Xunit;

/// <summary>
/// Compliance tests for daemon assignment persistence. Derived classes provide a database-specific
/// <see cref="IDaemonAssignmentPersistence"/> instance.
/// </summary>
[Trait("Category", "Integration")]
public abstract class DaemonAssignmentCompliance : IAsyncLifetime
{
    private static readonly Guid NodeA = Guid.NewGuid();
    private static readonly Guid NodeB = Guid.NewGuid();
    private const string DaemonId = "test:daemon";
    private const string DaemonId2 = "test:daemon-2";

    /// <summary>
    /// Gets the persistence implementation under test.
    /// </summary>
    protected abstract IDaemonAssignmentPersistence Persistence { get; }

    /// <inheritdoc/>
    public abstract Task InitializeAsync();

    /// <inheritdoc/>
    public abstract Task DisposeAsync();

    // --- Group 1: Basic Assignment ---

    [Fact]
    public async Task GivenNoPriorAssignment_WhenAssign_ThenCreatesWithGeneration1()
    {
        // Arrange & Act
        var assignment = await Persistence.AssignAsync(DaemonId, NodeA, CancellationToken.None);

        // Assert
        Assert.Equal(DaemonId, assignment.DaemonId);
        Assert.Equal(NodeA, assignment.AssignedNodeId);
        Assert.Equal(1, assignment.Generation);
        Assert.Equal(DaemonAssignmentState.Assigning, assignment.State);
        Assert.Null(assignment.DrainDeadline);
    }

    [Fact]
    public async Task GivenExistingAssignment_WhenReassign_ThenIncrementsGeneration()
    {
        // Arrange
        var first = await Persistence.AssignAsync(DaemonId, NodeA, CancellationToken.None);

        // Act
        var second = await Persistence.AssignAsync(DaemonId, NodeB, CancellationToken.None);

        // Assert
        Assert.Equal(first.Generation + 1, second.Generation);
        Assert.Equal(NodeB, second.AssignedNodeId);
        Assert.Equal(DaemonAssignmentState.Assigning, second.State);
    }

    [Fact]
    public async Task GivenMultipleAssignments_WhenGetAll_ThenReturnsAll()
    {
        // Arrange
        await Persistence.AssignAsync(DaemonId, NodeA, CancellationToken.None);
        await Persistence.AssignAsync(DaemonId2, NodeB, CancellationToken.None);

        // Act
        var all = await Persistence.GetAllAssignmentsAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, all.Count);
        Assert.Contains(all, a => a.DaemonId == DaemonId);
        Assert.Contains(all, a => a.DaemonId == DaemonId2);
    }

    [Fact]
    public async Task GivenAssignmentsOnDifferentNodes_WhenGetNodeAssignments_ThenFiltersCorrectly()
    {
        // Arrange
        await Persistence.AssignAsync(DaemonId, NodeA, CancellationToken.None);
        await Persistence.AssignAsync(DaemonId2, NodeB, CancellationToken.None);

        // Act
        var nodeAAssignments = await Persistence.GetNodeAssignmentsAsync(NodeA, CancellationToken.None);

        // Assert
        Assert.Single(nodeAAssignments);
        Assert.Equal(DaemonId, nodeAAssignments[0].DaemonId);
    }

    // --- Group 2: Acknowledge ---

    [Fact]
    public async Task GivenAssigningState_WhenAcknowledge_ThenTransitionsToActive()
    {
        // Arrange
        var assignment = await Persistence.AssignAsync(DaemonId, NodeA, CancellationToken.None);

        // Act
        var acknowledged = await Persistence.AcknowledgeAsync(DaemonId, assignment.Generation, CancellationToken.None);

        // Assert
        Assert.True(acknowledged);

        var all = await Persistence.GetAllAssignmentsAsync(CancellationToken.None);
        var updated = Assert.Single(all);
        Assert.Equal(DaemonAssignmentState.Active, updated.State);
    }

    [Fact]
    public async Task GivenStaleGeneration_WhenAcknowledge_ThenReturnsFalse()
    {
        // Arrange
        var first = await Persistence.AssignAsync(DaemonId, NodeA, CancellationToken.None);
        await Persistence.AssignAsync(DaemonId, NodeB, CancellationToken.None); // Overwrites with gen+1

        // Act
        var acknowledged = await Persistence.AcknowledgeAsync(DaemonId, first.Generation, CancellationToken.None);

        // Assert
        Assert.False(acknowledged);
    }

    [Fact]
    public async Task GivenActiveState_WhenAcknowledge_ThenReturnsFalse()
    {
        // Arrange
        var assignment = await Persistence.AssignAsync(DaemonId, NodeA, CancellationToken.None);
        await Persistence.AcknowledgeAsync(DaemonId, assignment.Generation, CancellationToken.None);

        // Act — try to acknowledge again when already Active
        var acknowledged = await Persistence.AcknowledgeAsync(DaemonId, assignment.Generation, CancellationToken.None);

        // Assert
        Assert.False(acknowledged);
    }

    // --- Group 3: Revoke ---

    [Fact]
    public async Task GivenActiveAssignment_WhenRevoke_ThenTransitionsToRevoking()
    {
        // Arrange
        var assignment = await Persistence.AssignAsync(DaemonId, NodeA, CancellationToken.None);
        await Persistence.AcknowledgeAsync(DaemonId, assignment.Generation, CancellationToken.None);

        // Act
        var revoked = await Persistence.RevokeAsync(DaemonId, TimeSpan.FromSeconds(30), CancellationToken.None);

        // Assert
        Assert.NotNull(revoked);
        Assert.Equal(DaemonAssignmentState.Revoking, revoked.State);
        Assert.Equal(assignment.Generation + 1, revoked.Generation);
        Assert.NotNull(revoked.DrainDeadline);
    }

    [Fact]
    public async Task GivenNonExistentDaemon_WhenRevoke_ThenReturnsNull()
    {
        // Act
        var result = await Persistence.RevokeAsync("nonexistent", TimeSpan.FromSeconds(30), CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    // --- Group 4: ConfirmDrain ---

    [Fact]
    public async Task GivenRevokingState_WhenConfirmDrain_ThenDeletesAssignment()
    {
        // Arrange
        var assignment = await Persistence.AssignAsync(DaemonId, NodeA, CancellationToken.None);
        await Persistence.AcknowledgeAsync(DaemonId, assignment.Generation, CancellationToken.None);
        var revoked = await Persistence.RevokeAsync(DaemonId, TimeSpan.FromSeconds(30), CancellationToken.None);

        // Act
        var confirmed = await Persistence.ConfirmDrainAsync(DaemonId, revoked!.Generation, CancellationToken.None);

        // Assert
        Assert.True(confirmed);

        var all = await Persistence.GetAllAssignmentsAsync(CancellationToken.None);
        Assert.Empty(all);
    }

    [Fact]
    public async Task GivenStaleGeneration_WhenConfirmDrain_ThenReturnsFalse()
    {
        // Arrange
        var assignment = await Persistence.AssignAsync(DaemonId, NodeA, CancellationToken.None);
        await Persistence.AcknowledgeAsync(DaemonId, assignment.Generation, CancellationToken.None);
        await Persistence.RevokeAsync(DaemonId, TimeSpan.FromSeconds(30), CancellationToken.None);

        // Act — use the original (stale) generation
        var confirmed = await Persistence.ConfirmDrainAsync(DaemonId, assignment.Generation, CancellationToken.None);

        // Assert
        Assert.False(confirmed);
    }

    // --- Group 5: Full Lifecycle ---

    [Fact]
    public async Task GivenFullLifecycle_WhenAssignAcknowledgeRevokeDrain_ThenCompletesSuccessfully()
    {
        // Arrange & Act — Assign
        var assigned = await Persistence.AssignAsync(DaemonId, NodeA, CancellationToken.None);
        Assert.Equal(DaemonAssignmentState.Assigning, assigned.State);

        // Acknowledge
        var acked = await Persistence.AcknowledgeAsync(DaemonId, assigned.Generation, CancellationToken.None);
        Assert.True(acked);

        // Verify Active
        var active = (await Persistence.GetAllAssignmentsAsync(CancellationToken.None))[0];
        Assert.Equal(DaemonAssignmentState.Active, active.State);

        // Revoke
        var revoked = await Persistence.RevokeAsync(DaemonId, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.NotNull(revoked);
        Assert.Equal(DaemonAssignmentState.Revoking, revoked.State);

        // Confirm drain
        var drained = await Persistence.ConfirmDrainAsync(DaemonId, revoked.Generation, CancellationToken.None);
        Assert.True(drained);

        // Assert — assignment is gone
        var remaining = await Persistence.GetAllAssignmentsAsync(CancellationToken.None);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task GivenReassignment_WhenOldNodeAcknowledges_ThenRejectedByGeneration()
    {
        // Arrange — Assign to NodeA
        var assignedToA = await Persistence.AssignAsync(DaemonId, NodeA, CancellationToken.None);

        // Reassign to NodeB (leader detected NodeA as dead)
        var assignedToB = await Persistence.AssignAsync(DaemonId, NodeB, CancellationToken.None);
        Assert.True(assignedToB.Generation > assignedToA.Generation);

        // Act — NodeA tries to acknowledge with stale generation
        var oldAck = await Persistence.AcknowledgeAsync(DaemonId, assignedToA.Generation, CancellationToken.None);

        // Assert — stale acknowledgment rejected
        Assert.False(oldAck);

        // NodeB can still acknowledge
        var newAck = await Persistence.AcknowledgeAsync(DaemonId, assignedToB.Generation, CancellationToken.None);
        Assert.True(newAck);
    }
}

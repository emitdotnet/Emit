namespace Emit.Abstractions.Daemon;

/// <summary>
/// Persistence operations for daemon assignment management.
/// </summary>
/// <remarks>
/// Implemented by each persistence provider (MongoDB, PostgreSQL) with
/// database-specific atomic operations to prevent split-assignment.
/// </remarks>
public interface IDaemonAssignmentPersistence
{
    /// <summary>
    /// Atomically assigns or reassigns a daemon to a node. If no assignment exists,
    /// creates one with generation 1. If an assignment exists, overwrites it with
    /// an incremented generation.
    /// </summary>
    /// <param name="daemonId">The daemon to assign.</param>
    /// <param name="nodeId">The target node.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created or updated assignment.</returns>
    Task<DaemonAssignment> AssignAsync(
        string daemonId,
        Guid nodeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions an assignment to the <see cref="DaemonAssignmentState.Revoking"/> state
    /// with an incremented generation and a drain deadline.
    /// </summary>
    /// <param name="daemonId">The daemon to revoke.</param>
    /// <param name="drainTimeout">Time allowed for the node to drain.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated assignment, or <c>null</c> if no assignment exists.</returns>
    Task<DaemonAssignment?> RevokeAsync(
        string daemonId,
        TimeSpan drainTimeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads all daemon assignments. Used by the leader for reconciliation.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All current assignments.</returns>
    Task<IReadOnlyList<DaemonAssignment>> GetAllAssignmentsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads all assignments for a specific node.
    /// </summary>
    /// <param name="nodeId">The node to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Assignments for the specified node.</returns>
    Task<IReadOnlyList<DaemonAssignment>> GetNodeAssignmentsAsync(
        Guid nodeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions an assignment from <see cref="DaemonAssignmentState.Assigning"/> to
    /// <see cref="DaemonAssignmentState.Active"/>. Uses compare-and-swap on the generation
    /// to prevent stale acknowledgements.
    /// </summary>
    /// <param name="daemonId">The daemon to acknowledge.</param>
    /// <param name="generation">The expected generation (fencing token).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the acknowledgement succeeded; <c>false</c> if the generation was stale.</returns>
    Task<bool> AcknowledgeAsync(
        string daemonId,
        long generation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a revoking assignment after the node has finished draining. Uses
    /// compare-and-swap on the generation and state to prevent stale confirmations.
    /// </summary>
    /// <param name="daemonId">The daemon whose drain is complete.</param>
    /// <param name="generation">The expected generation (fencing token).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the deletion succeeded; <c>false</c> if the generation was stale or state was wrong.</returns>
    Task<bool> ConfirmDrainAsync(
        string daemonId,
        long generation,
        CancellationToken cancellationToken = default);
}

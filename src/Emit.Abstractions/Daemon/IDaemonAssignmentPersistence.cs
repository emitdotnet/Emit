namespace Emit.Abstractions.Daemon;

/// <summary>
/// Persistence operations for daemon assignment management.
/// </summary>
/// <remarks>
/// <para>
/// Implemented by each persistence provider (MongoDB, PostgreSQL) with
/// database-specific atomic operations to prevent split-assignment.
/// </para>
/// <para>
/// Assignments are scoped to a service name. The same daemon identifier can be assigned
/// independently in each service that shares the database.
/// </para>
/// <para>
/// The generation of a daemon's assignment is a fencing token and never repeats: an assignment that is
/// drained or removed is released, and assigning the daemon again continues from its last generation.
/// </para>
/// </remarks>
public interface IDaemonAssignmentPersistence
{
    /// <summary>
    /// Atomically assigns a daemon of a service to a node, provided the current assignment still is
    /// <paramref name="expected"/>. A new assignment starts at generation 1; a reassignment increments
    /// the generation. Either way the assignment is in the <see cref="DaemonAssignmentState.Assigning"/> state.
    /// </summary>
    /// <param name="serviceName">The service the daemon belongs to.</param>
    /// <param name="daemonId">The daemon to assign.</param>
    /// <param name="nodeId">The target node.</param>
    /// <param name="expected">
    /// The assignment the caller based its decision on, or <c>null</c> when it found none. Nothing is written
    /// when the current assignment's generation or state differs, or when an assignment exists although
    /// none was expected.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new assignment, or <c>null</c> when the expectation no longer held.</returns>
    Task<DaemonAssignment?> AssignAsync(
        string serviceName,
        string daemonId,
        Guid nodeId,
        DaemonAssignment? expected,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions an assignment to the <see cref="DaemonAssignmentState.Revoking"/> state with an
    /// incremented generation, provided its generation still is <paramref name="expectedGeneration"/>.
    /// </summary>
    /// <param name="serviceName">The service the daemon belongs to.</param>
    /// <param name="daemonId">The daemon to revoke.</param>
    /// <param name="expectedGeneration">The generation the caller based its decision on (fencing token).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated assignment, or <c>null</c> when no assignment with that generation exists.</returns>
    Task<DaemonAssignment?> RevokeAsync(
        string serviceName,
        string daemonId,
        long expectedGeneration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads all daemon assignments of a service. Used by the leader for reconciliation.
    /// </summary>
    /// <param name="serviceName">The service whose assignments are returned.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All current assignments of the service.</returns>
    Task<IReadOnlyList<DaemonAssignment>> GetAllAssignmentsAsync(
        string serviceName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads all assignments for a specific node.
    /// </summary>
    /// <param name="serviceName">The service the node belongs to. Assignments of other services are not returned,
    /// even when they name the same node.</param>
    /// <param name="nodeId">The node to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Assignments of the service for the specified node.</returns>
    Task<IReadOnlyList<DaemonAssignment>> GetNodeAssignmentsAsync(
        string serviceName,
        Guid nodeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions an assignment from <see cref="DaemonAssignmentState.Assigning"/> to
    /// <see cref="DaemonAssignmentState.Active"/>. Uses compare-and-swap on the generation
    /// to prevent stale acknowledgements.
    /// </summary>
    /// <param name="serviceName">The service the daemon belongs to.</param>
    /// <param name="daemonId">The daemon to acknowledge.</param>
    /// <param name="generation">The expected generation (fencing token).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the acknowledgement succeeded; <c>false</c> if the generation was stale.</returns>
    Task<bool> AcknowledgeAsync(
        string serviceName,
        string daemonId,
        long generation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a revoking assignment after the node has finished draining. Uses
    /// compare-and-swap on the generation and state to prevent stale confirmations.
    /// </summary>
    /// <param name="serviceName">The service the daemon belongs to.</param>
    /// <param name="daemonId">The daemon whose drain is complete.</param>
    /// <param name="generation">The expected generation (fencing token).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the assignment was released; <c>false</c> if the generation was stale or state was wrong.</returns>
    Task<bool> ConfirmDrainAsync(
        string serviceName,
        string daemonId,
        long generation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases an assignment, for a daemon that no live node of the service is able to run or that its
    /// node gives up. Uses compare-and-swap on the generation so a concurrent reassignment is never released.
    /// </summary>
    /// <param name="serviceName">The service the daemon belongs to.</param>
    /// <param name="daemonId">The daemon whose assignment is released.</param>
    /// <param name="generation">The expected generation (fencing token).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the assignment was released; <c>false</c> if the generation was stale.</returns>
    Task<bool> RemoveAsync(
        string serviceName,
        string daemonId,
        long generation,
        CancellationToken cancellationToken = default);
}

namespace Emit.Abstractions.LeaderElection;

/// <summary>
/// Persistence operations for leader election and node registration.
/// </summary>
/// <remarks>
/// Implemented by each persistence provider (MongoDB, PostgreSQL) with
/// database-specific atomic operations to prevent split-brain.
/// </remarks>
public interface ILeaderElectionPersistence
{
    /// <summary>
    /// Registers or refreshes this node's heartbeat and attempts to acquire or renew the leader lease.
    /// </summary>
    /// <param name="request">The heartbeat parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result indicating whether this node is the leader.</returns>
    Task<HeartbeatResult> HeartbeatAsync(
        HeartbeatRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resigns the leader role if this node currently holds it.
    /// </summary>
    /// <param name="nodeId">The node identifier of the resigning leader.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ResignLeadershipAsync(
        Guid nodeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes nodes whose heartbeat has not been updated within the specified TTL.
    /// </summary>
    /// <param name="nodeRegistrationTtl">Nodes with a last-seen time older than this duration are removed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The node identifiers that were removed.</returns>
    Task<IReadOnlyList<Guid>> RemoveExpiredNodesAsync(
        TimeSpan nodeRegistrationTtl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes this node's registration record from the store.
    /// </summary>
    /// <param name="nodeId">The node identifier to deregister.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeregisterNodeAsync(
        Guid nodeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the identifiers of all currently registered nodes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The node identifiers of all registered nodes.</returns>
    Task<IReadOnlyList<Guid>> GetActiveNodeIdsAsync(
        CancellationToken cancellationToken = default);
}

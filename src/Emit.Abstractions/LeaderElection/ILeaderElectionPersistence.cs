namespace Emit.Abstractions.LeaderElection;

/// <summary>
/// Persistence operations for leader election and node registration.
/// </summary>
/// <remarks>
/// <para>
/// Implemented by each persistence provider (MongoDB, PostgreSQL) with
/// database-specific atomic operations to prevent split-brain.
/// </para>
/// <para>
/// Every operation is scoped to a service name. Services that share a database each have
/// their own leader and their own set of registered nodes.
/// </para>
/// <para>
/// No decision depends on a time the database records: whether a lease or a registration is still renewed is
/// judged by the nodes, from the heartbeat each one records, on their own clocks. A step in the database's clock
/// therefore cannot expire a lease or a registration early.
/// </para>
/// </remarks>
public interface ILeaderElectionPersistence
{
    /// <summary>
    /// Registers or refreshes this node's heartbeat and attempts to obtain the leader lease of its service, in
    /// one atomic step on the lease: the node acquires it if the service has none, renews it if it holds it, or
    /// takes it over if it is still recorded with <see cref="HeartbeatRequest.TakeOverLeaseHeartbeatId"/>.
    /// </summary>
    /// <param name="request">The heartbeat parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result indicating whether this node is the leader of its service.</returns>
    Task<HeartbeatResult> HeartbeatAsync(
        HeartbeatRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resigns the leader role of the service if this node holds it through a lease written by one of
    /// <paramref name="heartbeatIds"/>. A resign that reaches the database late therefore never releases a
    /// lease the node renewed after sending it.
    /// </summary>
    /// <param name="serviceName">The service whose leadership is resigned.</param>
    /// <param name="nodeId">The node identifier of the resigning leader.</param>
    /// <param name="heartbeatIds">
    /// The heartbeats that may have written the lease: the node's last successful heartbeat and those that
    /// failed since.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ResignLeadershipAsync(
        string serviceName,
        Guid nodeId,
        IReadOnlyCollection<Guid> heartbeatIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the given registrations of the service, each only if it is still recorded with the same
    /// heartbeat, so a node that heartbeated since is neither removed nor reported.
    /// </summary>
    /// <param name="serviceName">The service whose nodes are removed.</param>
    /// <param name="nodes">The registrations to remove, as they were read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The node identifiers that were removed. A removal that fails or is cancelled is left out of the result rather
    /// than failing the others; the caller tries it again.
    /// </returns>
    Task<IReadOnlyList<Guid>> RemoveNodesAsync(
        string serviceName,
        IReadOnlyCollection<NodeRegistration> nodes,
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
    /// Returns the registered nodes of a service, with the daemons each one is able to run and its last
    /// heartbeat.
    /// </summary>
    /// <param name="serviceName">The service whose nodes are returned.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registered nodes of the service.</returns>
    Task<IReadOnlyList<NodeRegistration>> GetNodesAsync(
        string serviceName,
        CancellationToken cancellationToken = default);
}

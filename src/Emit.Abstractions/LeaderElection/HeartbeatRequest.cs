namespace Emit.Abstractions.LeaderElection;

/// <summary>
/// Contains the parameters for a heartbeat operation.
/// </summary>
/// <param name="ServiceName">
/// The name of the service the node belongs to. Leader election, node registration and daemon
/// assignment are scoped to this name, so services sharing a database each elect their own leader.
/// </param>
/// <param name="NodeId">The unique identifier of the node sending the heartbeat.</param>
/// <param name="InstanceId">A human-readable identifier for the node (e.g., machine name).</param>
/// <param name="DaemonIds">The identifiers of the daemons this node is able to run.</param>
/// <param name="LeaseDuration">
/// How long the lease lasts without renewal. It is recorded with the lease, so other nodes wait this long, on
/// their own clocks, before taking it over.
/// </param>
/// <param name="NodeTtl">
/// How long the node's registration lasts without a heartbeat. It is recorded with the registration, so the
/// leader waits this long, on its own clock, before considering the node dead.
/// </param>
/// <param name="HeartbeatId">
/// Identifies this heartbeat. The lease and the registration this heartbeat writes record it, so other nodes can
/// tell whether they were renewed, and a resign can tell the lease apart from one renewed after it was sent.
/// </param>
/// <param name="TakeOverLeaseHeartbeatId">
/// The heartbeat of a lease this node saw go unrenewed for its whole duration. The node takes the lease over
/// only if it is still the one recorded; <c>null</c> to only acquire a lease the service does not have.
/// </param>
/// <param name="ClaimsLease">
/// Whether the heartbeat acquires, renews or takes over the lease. A heartbeat that does not only registers the
/// node and reports the lease, and never reports this node as leader: a node sends one for a lease duration after a
/// heartbeat of its failed, since that heartbeat may have written the lease without the node knowing, and a node
/// whose link keeps failing would otherwise keep other nodes from ever seeing it go unrenewed.
/// </param>
public sealed record HeartbeatRequest(
    string ServiceName,
    Guid NodeId,
    string InstanceId,
    IReadOnlyList<string> DaemonIds,
    TimeSpan LeaseDuration,
    TimeSpan NodeTtl,
    Guid HeartbeatId,
    Guid? TakeOverLeaseHeartbeatId = null,
    bool ClaimsLease = true);

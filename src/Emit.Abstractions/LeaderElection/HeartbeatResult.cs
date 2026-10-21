namespace Emit.Abstractions.LeaderElection;

/// <summary>
/// The result of a heartbeat operation.
/// </summary>
/// <param name="IsLeader">Whether the requesting node is the current leader of its service after this heartbeat.</param>
/// <param name="LeaderNodeId">The unique identifier of the current leader node of the service.</param>
/// <param name="LeaseHeartbeatId">
/// The heartbeat that last acquired or renewed the service's lease. It can be an earlier heartbeat of the same
/// node that reached the database late, which the node must name to resign the lease. Other nodes watch it to
/// tell whether the lease is still being renewed.
/// </param>
/// <param name="LeaseDuration">How long the current lease lasts without renewal, as its holder requested.</param>
public sealed record HeartbeatResult(
    bool IsLeader,
    Guid LeaderNodeId,
    Guid LeaseHeartbeatId,
    TimeSpan LeaseDuration);

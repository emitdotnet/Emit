namespace Emit.Abstractions.LeaderElection;

/// <summary>
/// The result of a heartbeat operation.
/// </summary>
/// <param name="IsLeader">Whether the requesting node is the current leader after this heartbeat.</param>
/// <param name="LeaderNodeId">The unique identifier of the current leader node.</param>
public sealed record HeartbeatResult(
    bool IsLeader,
    Guid LeaderNodeId);

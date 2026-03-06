namespace Emit.Abstractions.LeaderElection;

/// <summary>
/// Contains the parameters for a heartbeat operation.
/// </summary>
/// <param name="NodeId">The unique identifier of the node sending the heartbeat.</param>
/// <param name="InstanceId">A human-readable identifier for the node (e.g., machine name).</param>
/// <param name="LeaseDuration">The duration of the leader lease to request or renew.</param>
/// <param name="NodeTtl">The time-to-live for the node registration record.</param>
public sealed record HeartbeatRequest(
    Guid NodeId,
    string InstanceId,
    TimeSpan LeaseDuration,
    TimeSpan NodeTtl);

namespace Emit.Abstractions.LeaderElection;

/// <summary>
/// A node registered in a service, with the daemons it is able to run.
/// </summary>
/// <param name="NodeId">The unique identifier of the node.</param>
/// <param name="DaemonIds">The identifiers of the daemons the node declared on its last heartbeat.</param>
/// <param name="HeartbeatId">
/// The node's last heartbeat. The leader watches it to tell whether the node is still heartbeating.
/// </param>
/// <param name="RegistrationTtl">How long the registration lasts without a heartbeat, as the node requested.</param>
public sealed record NodeRegistration(
    Guid NodeId,
    IReadOnlyList<string> DaemonIds,
    Guid HeartbeatId,
    TimeSpan RegistrationTtl);

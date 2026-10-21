namespace Emit.Abstractions.Daemon;

/// <summary>
/// Represents the assignment of a daemon to a cluster node.
/// </summary>
/// <param name="DaemonId">The unique identifier of the daemon.</param>
/// <param name="AssignedNodeId">The node that is assigned to run this daemon.</param>
/// <param name="Generation">
/// Fencing token incremented on every assignment change. Used to prevent
/// stale nodes from acknowledging or draining outdated assignments.
/// </param>
/// <param name="State">The current state of the assignment.</param>
/// <param name="AssignedAt">When the current assignment was created (UTC).</param>
/// <param name="FencingKey">
/// Identifies this daemon of this service in this database, the same for every assignment of it. A system the
/// daemon writes to can key the fencing of earlier runs on it, together with <paramref name="Generation"/>, and no
/// other database's daemon shares it, even one of a service with the same name.
/// </param>
public sealed record DaemonAssignment(
    string DaemonId,
    Guid AssignedNodeId,
    long Generation,
    DaemonAssignmentState State,
    DateTime AssignedAt,
    Guid FencingKey = default);

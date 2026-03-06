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
/// <param name="DrainDeadline">
/// When set, the deadline by which the node must complete draining. Only set
/// when <paramref name="State"/> is <see cref="DaemonAssignmentState.Revoking"/>.
/// </param>
public sealed record DaemonAssignment(
    string DaemonId,
    Guid AssignedNodeId,
    long Generation,
    DaemonAssignmentState State,
    DateTime AssignedAt,
    DateTime? DrainDeadline);

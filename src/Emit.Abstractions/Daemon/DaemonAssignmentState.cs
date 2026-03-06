namespace Emit.Abstractions.Daemon;

/// <summary>
/// The state of a daemon assignment in the cluster.
/// </summary>
public enum DaemonAssignmentState
{
    /// <summary>
    /// The leader has assigned the daemon to a node, but the node has not yet acknowledged it.
    /// </summary>
    Assigning,

    /// <summary>
    /// The node has acknowledged the assignment and the daemon is running.
    /// </summary>
    Active,

    /// <summary>
    /// The leader has revoked the assignment and the node is draining (stopping) the daemon.
    /// </summary>
    Revoking
}

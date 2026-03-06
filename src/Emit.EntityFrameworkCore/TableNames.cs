namespace Emit.EntityFrameworkCore;

/// <summary>
/// Table name constants for the outbox persistence schema.
/// </summary>
internal static class TableNames
{
    /// <summary>
    /// The table name for outbox entries.
    /// </summary>
    internal const string Outbox = "emit_outbox";

    /// <summary>
    /// The table name for distributed locks.
    /// </summary>
    internal const string Locks = "emit_locks";

    /// <summary>
    /// The table name for the leader election row.
    /// </summary>
    internal const string Leader = "emit_leader";

    /// <summary>
    /// The table name for registered nodes.
    /// </summary>
    internal const string Nodes = "emit_nodes";

    /// <summary>
    /// The table name for daemon assignments.
    /// </summary>
    internal const string DaemonAssignments = "emit_daemon_assignments";
}

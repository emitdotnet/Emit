namespace Emit.MongoDB;

/// <summary>
/// MongoDB collection names used by the Emit persistence provider.
/// </summary>
internal static class CollectionNames
{
    /// <summary>
    /// The collection name for outbox entries.
    /// </summary>
    internal const string Outbox = "emit.outbox";

    /// <summary>
    /// The collection name for sequence counters.
    /// </summary>
    internal const string Sequence = "emit.sequences";

    /// <summary>
    /// The collection name for distributed locks.
    /// </summary>
    internal const string Locks = "emit.locks";

    /// <summary>
    /// The collection name for the leader election document.
    /// </summary>
    internal const string Leader = "emit.leader";

    /// <summary>
    /// The collection name for registered nodes.
    /// </summary>
    internal const string Nodes = "emit.nodes";

    /// <summary>
    /// The collection name for daemon assignments.
    /// </summary>
    internal const string DaemonAssignments = "emit.daemon.assignments";
}

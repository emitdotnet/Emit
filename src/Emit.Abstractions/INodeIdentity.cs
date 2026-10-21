namespace Emit.Abstractions;

/// <summary>
/// Provides the unique identity assigned to this application instance at startup.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="NodeId"/> is generated once when the process starts and remains stable
/// for its lifetime. All subsystems (metrics, tracing, outbox entries, and leader
/// election) reference this single source of truth so every observation is attributable
/// to its origin node.
/// </para>
/// <para>
/// The default implementation generates a random <see cref="Guid"/>. A custom implementation, registered
/// before calling <c>AddEmit</c>, must never give the same identifier to two processes running at the same
/// time: leader election and daemon assignment identify a node by it, so two processes sharing one would
/// both lead and both run the same daemons. An identifier may come back in a later process, such as one derived from
/// a pod name: that process gives up what the earlier one was running instead of resuming it.
/// </para>
/// </remarks>
public interface INodeIdentity
{
    /// <summary>
    /// Gets the unique identifier for this node, stable for the lifetime of the process. Never
    /// <see cref="Guid.Empty"/>: a host whose identity returns it fails to start leader election.
    /// </summary>
    Guid NodeId { get; }

    /// <summary>
    /// Gets the name of the service this node belongs to.
    /// </summary>
    /// <remarks>
    /// Every replica of the same application shares this name. Leader election, daemon assignment
    /// and outbox processing are scoped to it, so services sharing a database never coordinate
    /// each other's work.
    /// </remarks>
    string ServiceName { get; }
}

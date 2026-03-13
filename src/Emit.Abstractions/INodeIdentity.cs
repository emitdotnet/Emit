namespace Emit.Abstractions;

/// <summary>
/// Provides the unique identity assigned to this application instance at startup.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="NodeId"/> is generated once when the process starts and remains stable
/// for its lifetime. All subsystems — metrics, tracing, outbox entries, and leader
/// election — reference this single source of truth so every observation is attributable
/// to its origin node.
/// </para>
/// <para>
/// The default implementation generates a random <see cref="Guid"/>. Register a custom
/// implementation before calling <c>AddEmit</c> to use a deterministic or externally
/// assigned identifier.
/// </para>
/// </remarks>
public interface INodeIdentity
{
    /// <summary>
    /// Gets the unique identifier for this node, stable for the lifetime of the process.
    /// </summary>
    Guid NodeId { get; }
}

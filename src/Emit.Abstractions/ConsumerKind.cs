namespace Emit.Abstractions;

/// <summary>
/// Identifies the kind of consumer processing a message in a fan-out pipeline.
/// </summary>
public enum ConsumerKind
{
    /// <summary>
    /// A directly registered consumer handler (e.g., via <c>AddConsumer&lt;T&gt;()</c>).
    /// </summary>
    Direct,

    /// <summary>
    /// A content-based message router that dispatches to one of several sub-consumers
    /// based on a route key extracted from the message.
    /// </summary>
    Router,
}

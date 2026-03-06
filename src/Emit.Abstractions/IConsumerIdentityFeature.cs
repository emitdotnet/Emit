namespace Emit.Abstractions;

/// <summary>
/// Identifies the consumer processing the current message. Set on the context's feature
/// collection before the pipeline is invoked, and updated by routers after route selection.
/// Available to tracing, metrics, and error handling middleware throughout the pipeline.
/// </summary>
public interface IConsumerIdentityFeature
{
    /// <summary>
    /// The consumer identifier. For direct consumers this is the type name
    /// (e.g., <c>"OrderCreatedConsumer"</c>). For routers this is the user-provided
    /// identifier passed to <c>AddRouter</c>.
    /// </summary>
    string Identifier { get; }

    /// <summary>
    /// Whether this is a direct consumer or a content-based router.
    /// </summary>
    ConsumerKind Kind { get; }

    /// <summary>
    /// The consumer handler type processing the message. For direct consumers this is
    /// the registered handler type. For routers this is the selected sub-consumer type
    /// after route selection, or <c>null</c> if no route matched.
    /// </summary>
    Type? ConsumerType { get; }

    /// <summary>
    /// The route key that was matched, or <c>null</c> for direct consumers
    /// and unmatched router invocations.
    /// </summary>
    object? RouteKey { get; }
}

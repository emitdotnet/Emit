namespace Emit.Pipeline;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;

/// <summary>
/// Pairs consumer identity metadata with its fully-composed middleware pipeline delegate.
/// Used by consumer workers to set <see cref="IConsumerIdentityFeature"/> before invoking
/// each pipeline.
/// </summary>
/// <typeparam name="TMessage">The message value type.</typeparam>
public sealed class ConsumerPipelineEntry<TMessage>
{
    /// <summary>
    /// The consumer identifier. For direct consumers this is the type name
    /// (e.g., <c>"OrderCreatedConsumer"</c>). For routers this is the user-provided identifier.
    /// </summary>
    public required string Identifier { get; init; }

    /// <summary>
    /// Whether this entry represents a direct consumer or a content-based router.
    /// </summary>
    public required ConsumerKind Kind { get; init; }

    /// <summary>
    /// The consumer handler type. For direct consumers this is the handler type.
    /// For routers this is <c>null</c> (the router invoker sets the specific type after selection).
    /// </summary>
    public Type? ConsumerType { get; init; }

    /// <summary>
    /// The fully-composed middleware pipeline delegate.
    /// </summary>
    public required MessageDelegate<InboundContext<TMessage>> Pipeline { get; init; }
}

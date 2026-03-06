namespace Emit.Routing;

using Emit.Abstractions.Pipeline;

/// <summary>
/// Holds the configuration for a single route registered via the router builder.
/// </summary>
internal sealed class RouteEntry<TMessage>
{
    /// <summary>The route key (type-erased from <c>TRouteKey</c>).</summary>
    public required object RouteKey { get; init; }

    /// <summary>The consumer handler type to dispatch to when this route matches.</summary>
    public required Type ConsumerType { get; init; }

    /// <summary>Optional per-route middleware pipeline, or <c>null</c> if no per-route middleware was configured.</summary>
    public IMessagePipelineBuilder? Pipeline { get; init; }
}

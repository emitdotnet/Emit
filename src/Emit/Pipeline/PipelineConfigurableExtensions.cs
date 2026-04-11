namespace Emit.Pipeline;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;

/// <summary>
/// Extension methods for pipeline configurable builders.
/// </summary>
public static class PipelineConfigurableExtensions
{
    /// <summary>
    /// Registers a middleware type in the builder's inbound pipeline. The type may be an open generic
    /// (e.g., <c>typeof(LoggingMiddleware&lt;&gt;)</c>) closed per message type at build time,
    /// or a closed type implementing <see cref="IMiddleware{TContext}"/>.
    /// Middleware executes in registration order (first registered = outermost in the pipeline).
    /// </summary>
    /// <typeparam name="TBuilder">The builder type, preserved for method chaining.</typeparam>
    /// <param name="builder">The builder exposing an inbound pipeline.</param>
    /// <param name="middlewareType">The middleware type (open or closed generic).</param>
    /// <param name="lifetime">Controls when the middleware instance is created. Defaults to <see cref="MiddlewareLifetime.Singleton"/>.</param>
    /// <returns>The builder for method chaining.</returns>
    public static TBuilder UseInbound<TBuilder>(
        this TBuilder builder,
        Type middlewareType,
        MiddlewareLifetime lifetime = default)
        where TBuilder : IInboundConfigurable
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.InboundPipeline.Use(middlewareType, lifetime);
        return builder;
    }

    /// <summary>
    /// Registers a middleware type in the builder's outbound pipeline. The type may be an open generic
    /// (e.g., <c>typeof(LoggingMiddleware&lt;&gt;)</c>) closed per message type at build time,
    /// or a closed type implementing <see cref="IMiddleware{TContext}"/>.
    /// Middleware executes in registration order (first registered = outermost in the pipeline).
    /// </summary>
    /// <typeparam name="TBuilder">The builder type, preserved for method chaining.</typeparam>
    /// <param name="builder">The builder exposing an outbound pipeline.</param>
    /// <param name="middlewareType">The middleware type (open or closed generic).</param>
    /// <param name="lifetime">Controls when the middleware instance is created. Defaults to <see cref="MiddlewareLifetime.Singleton"/>.</param>
    /// <returns>The builder for method chaining.</returns>
    public static TBuilder UseOutbound<TBuilder>(
        this TBuilder builder,
        Type middlewareType,
        MiddlewareLifetime lifetime = default)
        where TBuilder : IOutboundConfigurable
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.OutboundPipeline.Use(middlewareType, lifetime);
        return builder;
    }

    /// <summary>
    /// Registers a typed factory-based middleware in an inbound builder's pipeline.
    /// The <typeparamref name="TMessage"/> is inferred from the builder, ensuring the factory
    /// produces middleware compatible with the builder's consume context type.
    /// </summary>
    /// <typeparam name="TMessage">The message type, inferred from the builder.</typeparam>
    /// <param name="builder">The inbound builder exposing a pipeline.</param>
    /// <param name="factory">A factory that creates the typed middleware instance.</param>
    /// <param name="lifetime">Controls when the factory is invoked. Defaults to <see cref="MiddlewareLifetime.Singleton"/>.</param>
    /// <returns>The builder for method chaining.</returns>
    public static IInboundConfigurable<TMessage> Use<TMessage>(
        this IInboundConfigurable<TMessage> builder,
        Func<IServiceProvider, IMiddleware<ConsumeContext<TMessage>>> factory,
        MiddlewareLifetime lifetime = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.InboundPipeline.Use(factory, lifetime);
        return builder;
    }

    /// <summary>
    /// Registers a typed factory-based middleware in an outbound builder's pipeline.
    /// The <typeparamref name="TMessage"/> is inferred from the builder, ensuring the factory
    /// produces middleware compatible with the builder's outbound context type.
    /// </summary>
    /// <typeparam name="TMessage">The message type, inferred from the builder.</typeparam>
    /// <param name="builder">The outbound builder exposing a pipeline.</param>
    /// <param name="factory">A factory that creates the typed middleware instance.</param>
    /// <param name="lifetime">Controls when the factory is invoked. Defaults to <see cref="MiddlewareLifetime.Singleton"/>.</param>
    /// <returns>The builder for method chaining.</returns>
    public static IOutboundConfigurable<TMessage> Use<TMessage>(
        this IOutboundConfigurable<TMessage> builder,
        Func<IServiceProvider, IMiddleware<SendContext<TMessage>>> factory,
        MiddlewareLifetime lifetime = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.OutboundPipeline.Use(factory, lifetime);
        return builder;
    }
}

namespace Emit.Abstractions;

using Emit.Abstractions.Pipeline;

/// <summary>
/// Implemented by leaf builders that configure inbound (consumer/handler) pipelines for a known
/// message type. Provides compile-time safe middleware registration where the constraint ensures
/// the middleware implements <see cref="IMiddleware{TContext}"/> for <see cref="ConsumeContext{T}"/>.
/// </summary>
/// <typeparam name="TMessage">The message type processed by this builder's pipeline.</typeparam>
public interface IInboundConfigurable<TMessage> : IInboundConfigurable
{
    /// <summary>
    /// Registers a middleware type on this inbound pipeline.
    /// </summary>
    /// <typeparam name="TMiddleware">
    /// The middleware type. Must implement <see cref="IMiddleware{TContext}"/>
    /// for <see cref="ConsumeContext{T}"/> with the builder's message type.
    /// </typeparam>
    /// <param name="lifetime">Controls when the middleware instance is created.</param>
    /// <returns>This builder for chaining.</returns>
    IInboundConfigurable<TMessage> Use<TMiddleware>(MiddlewareLifetime lifetime = default)
        where TMiddleware : class, IMiddleware<ConsumeContext<TMessage>>;

    /// <summary>
    /// Registers a consumer filter that determines whether each message should continue
    /// through the pipeline. The filter is resolved from the scoped service provider
    /// for each message. If <typeparamref name="TFilter"/> is registered in DI, it is
    /// resolved from there; otherwise, a new instance is created with constructor injection.
    /// </summary>
    /// <typeparam name="TFilter">
    /// The filter type. Must implement <see cref="IConsumerFilter{TMessage}"/>.
    /// </typeparam>
    /// <returns>This builder for chaining.</returns>
    IInboundConfigurable<TMessage> Filter<TFilter>()
        where TFilter : class, IConsumerFilter<TMessage>;
}

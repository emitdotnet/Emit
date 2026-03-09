namespace Emit.Abstractions;

using Emit.Abstractions.Pipeline;

/// <summary>
/// Implemented by leaf builders that configure outbound (producer) pipelines for a known
/// message type. Provides compile-time safe middleware registration where the constraint ensures
/// the middleware implements <see cref="IMiddleware{TContext}"/> for <see cref="SendContext{T}"/>.
/// </summary>
/// <typeparam name="TMessage">The message type processed by this builder's pipeline.</typeparam>
public interface IOutboundConfigurable<TMessage> : IOutboundPipelineConfigurable
{
    /// <summary>
    /// Registers a middleware type on this outbound pipeline.
    /// </summary>
    /// <typeparam name="TMiddleware">
    /// The middleware type. Must implement <see cref="IMiddleware{TContext}"/>
    /// for <see cref="SendContext{T}"/> with the builder's message type.
    /// </typeparam>
    /// <param name="lifetime">Controls when the middleware instance is created.</param>
    /// <returns>This builder for chaining.</returns>
    IOutboundConfigurable<TMessage> Use<TMiddleware>(MiddlewareLifetime lifetime = default)
        where TMiddleware : class, IMiddleware<SendContext<TMessage>>;
}

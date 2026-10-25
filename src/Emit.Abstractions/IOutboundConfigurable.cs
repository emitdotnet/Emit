namespace Emit.Abstractions;

using Emit.Abstractions.Pipeline;

/// <summary>
/// Implemented by builders that configure the producer pipeline of a known message type.
/// </summary>
/// <typeparam name="TMessage">The message type processed by this builder's pipeline.</typeparam>
public interface IOutboundConfigurable<TMessage>
{
    /// <summary>
    /// Registers a middleware type on this outbound pipeline.
    /// </summary>
    /// <typeparam name="TMiddleware">
    /// The middleware type: an <see cref="IMiddleware{TContext}"/> of <see cref="SendContext{T}"/> with the
    /// builder's message type, or of a context it derives from.
    /// </typeparam>
    /// <param name="lifetime">Controls when the middleware instance is created.</param>
    /// <returns>This builder for chaining.</returns>
    IOutboundConfigurable<TMessage> Use<TMiddleware>(MiddlewareLifetime lifetime = default)
        where TMiddleware : class, IMiddleware<SendContext<TMessage>>;

    /// <summary>
    /// Registers a middleware created by a factory on this outbound pipeline.
    /// </summary>
    /// <param name="factory">Creates the middleware instance.</param>
    /// <param name="lifetime">Controls when the factory is invoked.</param>
    /// <returns>This builder for chaining.</returns>
    IOutboundConfigurable<TMessage> Use(
        Func<IServiceProvider, IMiddleware<SendContext<TMessage>>> factory,
        MiddlewareLifetime lifetime = default);
}

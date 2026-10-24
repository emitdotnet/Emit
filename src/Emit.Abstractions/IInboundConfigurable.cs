namespace Emit.Abstractions;

using Emit.Abstractions.Pipeline;

/// <summary>
/// Implemented by builders that configure the consumer pipelines of a known message type: consumer groups,
/// consumers and routes.
/// </summary>
/// <typeparam name="TMessage">The message type processed by this builder's pipeline.</typeparam>
public interface IInboundConfigurable<TMessage>
{
    /// <summary>
    /// Registers a middleware type on this inbound pipeline.
    /// </summary>
    /// <typeparam name="TMiddleware">
    /// The middleware type: an <see cref="IMiddleware{TContext}"/> of <see cref="ConsumeContext{T}"/> with the
    /// builder's message type, or of a context it derives from.
    /// </typeparam>
    /// <param name="lifetime">Controls when the middleware instance is created.</param>
    /// <returns>This builder for chaining.</returns>
    IInboundConfigurable<TMessage> Use<TMiddleware>(MiddlewareLifetime lifetime = default)
        where TMiddleware : class, IMiddleware<ConsumeContext<TMessage>>;

    /// <summary>
    /// Registers a middleware created by a factory on this inbound pipeline.
    /// </summary>
    /// <param name="factory">Creates the middleware instance.</param>
    /// <param name="lifetime">Controls when the factory is invoked.</param>
    /// <returns>This builder for chaining.</returns>
    IInboundConfigurable<TMessage> Use(
        Func<IServiceProvider, IMiddleware<ConsumeContext<TMessage>>> factory,
        MiddlewareLifetime lifetime = default);

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

    /// <summary>
    /// Registers an asynchronous predicate filter. When the predicate returns <see langword="false"/>,
    /// the pipeline is short-circuited and the consumer handler is not invoked.
    /// </summary>
    /// <param name="predicate">
    /// An asynchronous predicate that receives the consume context and a cancellation token.
    /// Return <see langword="true"/> to continue the pipeline, <see langword="false"/> to skip.
    /// </param>
    /// <returns>This builder for chaining.</returns>
    IInboundConfigurable<TMessage> Filter(
        Func<ConsumeContext<TMessage>, CancellationToken, ValueTask<bool>> predicate);
}

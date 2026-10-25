namespace Emit.Abstractions.Pipeline;

/// <summary>
/// A level of middleware for one kind of pipeline. Middleware registered here runs on every pipeline of this
/// level, in registration order (first registered is outermost), and must accept its context
/// <typeparamref name="TContext"/> or a context it derives from.
/// </summary>
/// <typeparam name="TContext">The context of the pipelines this level applies to.</typeparam>
public interface IMessagePipelineBuilder<TContext> where TContext : MessageContext
{
    /// <summary>
    /// Registers a middleware resolved from the service provider.
    /// </summary>
    /// <typeparam name="TMiddleware">The middleware type.</typeparam>
    /// <param name="lifetime">Controls when the middleware instance is created.</param>
    /// <returns>This builder for chaining.</returns>
    IMessagePipelineBuilder<TContext> Use<TMiddleware>(MiddlewareLifetime lifetime = default)
        where TMiddleware : class, IMiddleware<TContext>;

    /// <summary>
    /// Registers a middleware created by a factory.
    /// </summary>
    /// <param name="factory">Creates the middleware instance.</param>
    /// <param name="lifetime">Controls when the factory is invoked.</param>
    /// <returns>This builder for chaining.</returns>
    IMessagePipelineBuilder<TContext> Use(
        Func<IServiceProvider, IMiddleware<TContext>> factory,
        MiddlewareLifetime lifetime = default);

    /// <summary>
    /// Registers a generic middleware type definition, such as <c>typeof(ValidationMiddleware&lt;&gt;)</c>, closed
    /// with the message type of each pipeline. Use it for middleware that needs the message type; other middleware
    /// is registered with <see cref="Use{TMiddleware}"/>.
    /// </summary>
    /// <param name="middlewareType">A generic type definition with one type parameter, the message type.</param>
    /// <param name="lifetime">Controls when the middleware instance is created.</param>
    /// <returns>This builder for chaining.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="middlewareType"/> is not a generic type definition with one type parameter, or its closed
    /// form cannot run on the pipelines of this level.
    /// </exception>
    IMessagePipelineBuilder<TContext> Use(Type middlewareType, MiddlewareLifetime lifetime = default);
}

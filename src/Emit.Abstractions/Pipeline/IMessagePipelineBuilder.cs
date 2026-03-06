namespace Emit.Abstractions.Pipeline;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Collects middleware descriptors and composes them into a typed <see cref="MessageDelegate{TContext}"/>
/// chain at build time. Each builder (EmitBuilder, KafkaBuilder, MediatorBuilder, etc.) exposes
/// an instance via pipeline properties. Open generic middleware types are closed per message
/// type when <see cref="Build{TContext,TMessage}"/> is called.
/// </summary>
public interface IMessagePipelineBuilder
{
    /// <summary>
    /// Gets the registered middleware descriptors in registration order.
    /// </summary>
    IReadOnlyList<MiddlewareDescriptor> Descriptors { get; }

    /// <summary>
    /// Registers a middleware type with an explicit lifetime. The type may be an open generic
    /// (e.g., <c>typeof(LoggingMiddleware&lt;&gt;)</c>) that is closed per message type at build time,
    /// or a closed type that implements <see cref="IMiddleware{TContext}"/> for a specific context type.
    /// </summary>
    /// <param name="middlewareType">The middleware type (open or closed generic).</param>
    /// <param name="lifetime">Controls when the middleware instance is created.</param>
    /// <returns>This builder for chaining.</returns>
    IMessagePipelineBuilder Use(Type middlewareType, MiddlewareLifetime lifetime = default);

    /// <summary>
    /// Registers a closed middleware type. Use this for middleware that implements
    /// <see cref="IMiddleware{TContext}"/> for a specific context type. Direction compatibility
    /// is validated at build time.
    /// </summary>
    /// <typeparam name="TMiddleware">The middleware type to register.</typeparam>
    /// <param name="lifetime">Controls when the middleware instance is created.</param>
    /// <returns>This builder for chaining.</returns>
    IMessagePipelineBuilder Use<TMiddleware>(MiddlewareLifetime lifetime = default)
        where TMiddleware : class;

    /// <summary>
    /// Registers a typed factory-based middleware for a specific context type.
    /// </summary>
    /// <typeparam name="TContext">The pipeline context type.</typeparam>
    /// <param name="factory">A factory that creates the typed middleware instance.</param>
    /// <param name="lifetime">Controls when the factory is invoked.</param>
    /// <returns>This builder for chaining.</returns>
    IMessagePipelineBuilder Use<TContext>(
        Func<IServiceProvider, IMiddleware<TContext>> factory,
        MiddlewareLifetime lifetime = default)
        where TContext : MessageContext;

    /// <summary>
    /// Registers all collected middleware types in the service collection with the appropriate lifetime.
    /// Open generic types are registered as open generics — .NET DI resolves closed forms automatically.
    /// </summary>
    /// <param name="services">The service collection to register middleware types in.</param>
    void RegisterServices(IServiceCollection services);

    /// <summary>
    /// Flattens parent layers and this builder's middleware, closes open generic types with
    /// <typeparamref name="TMessage"/>, validates each closed type implements
    /// <see cref="IMiddleware{TContext}"/> for the target context, and composes the
    /// <see cref="MessageDelegate{TContext}"/> chain.
    /// </summary>
    /// <typeparam name="TContext">The pipeline context type (e.g., <c>InboundContext&lt;TValue&gt;</c>).</typeparam>
    /// <typeparam name="TMessage">The message type to close open generics with.</typeparam>
    /// <param name="services">The service provider for resolving middleware.</param>
    /// <param name="terminal">The terminal delegate (e.g., handler invoker).</param>
    /// <param name="parentLayers">Parent pipeline builders, outermost first.</param>
    /// <returns>A composed <see cref="MessageDelegate{TContext}"/> chain.</returns>
    MessageDelegate<TContext> Build<TContext, TMessage>(
        IServiceProvider services,
        MessageDelegate<TContext> terminal,
        params IMessagePipelineBuilder[] parentLayers)
        where TContext : MessageContext<TMessage>;
}

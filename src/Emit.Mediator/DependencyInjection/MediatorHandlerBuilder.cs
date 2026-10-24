namespace Emit.Mediator.DependencyInjection;

using Emit.Abstractions.Pipeline;
using Emit.Pipeline;

/// <summary>
/// Configures per-handler middleware for a mediator request handler.
/// Middleware registered here wraps only the specific handler it is attached to,
/// forming the innermost pipeline layer, inside the unit of work (every pipeline → mediator → unit of work →
/// per-handler → handler).
/// </summary>
/// <typeparam name="TRequest">The request type handled by the associated handler.</typeparam>
public sealed class MediatorHandlerBuilder<TRequest>
{
    /// <summary>
    /// Creates a new handler builder.
    /// </summary>
    internal MediatorHandlerBuilder()
    {
    }

    /// <summary>
    /// Gets the per-handler middleware level. Middleware registered here wraps only this specific handler.
    /// </summary>
    internal MessagePipelineBuilder<MediatorContext<TRequest>> Pipeline { get; } = new();

    /// <summary>
    /// Registers a middleware type on this handler's pipeline.
    /// </summary>
    /// <typeparam name="TMiddleware">
    /// The middleware type: an <see cref="IMiddleware{TContext}"/> of <see cref="MediatorContext{T}"/> with the
    /// handler's request type, or of a context it derives from.
    /// </typeparam>
    /// <param name="lifetime">Controls when the middleware instance is created.</param>
    /// <returns>This builder for chaining.</returns>
    public MediatorHandlerBuilder<TRequest> Use<TMiddleware>(MiddlewareLifetime lifetime = default)
        where TMiddleware : class, IMiddleware<MediatorContext<TRequest>>
    {
        Pipeline.Use<TMiddleware>(lifetime);
        return this;
    }
}

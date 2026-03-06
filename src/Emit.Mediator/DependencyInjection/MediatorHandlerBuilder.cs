namespace Emit.Mediator.DependencyInjection;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Pipeline;

/// <summary>
/// Configures per-handler middleware for a mediator request handler.
/// Middleware registered here wraps only the specific handler it is attached to,
/// forming the innermost pipeline layer (global → mediator → per-handler → terminal).
/// All registration methods constrain middleware to <typeparamref name="TRequest"/>,
/// providing compile-time type safety.
/// </summary>
/// <typeparam name="TRequest">The request type handled by the associated handler.</typeparam>
public sealed class MediatorHandlerBuilder<TRequest> : IInboundConfigurable<TRequest>
{
    /// <inheritdoc />
    IMessagePipelineBuilder IInboundPipelineConfigurable.InboundPipeline => Pipeline;

    /// <summary>
    /// Gets the per-handler middleware pipeline builder. Middleware registered here
    /// wraps only this specific handler.
    /// </summary>
    internal IMessagePipelineBuilder Pipeline { get; } = new MessagePipelineBuilder();

    /// <summary>
    /// Creates a new handler builder.
    /// </summary>
    internal MediatorHandlerBuilder()
    {
    }

    /// <inheritdoc />
    public IInboundConfigurable<TRequest> Use<TMiddleware>(MiddlewareLifetime lifetime = default)
        where TMiddleware : class, IMiddleware<InboundContext<TRequest>>
    {
        Pipeline.Use(typeof(TMiddleware), lifetime);
        return this;
    }

    /// <inheritdoc />
    public IInboundConfigurable<TRequest> Filter<TFilter>()
        where TFilter : class, IConsumerFilter<TRequest>
    {
        Pipeline.AddConsumerFilter<TRequest, TFilter>();
        return this;
    }
}

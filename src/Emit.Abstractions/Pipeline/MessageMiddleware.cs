namespace Emit.Abstractions.Pipeline;

/// <summary>
/// Optional base class for cross-cutting middleware that runs on both inbound and outbound pipelines.
/// Implements <see cref="IMiddleware{TContext}"/> for both <see cref="InboundContext{T}"/> and
/// <see cref="OutboundContext{T}"/>, dispatching to a single <see cref="InvokeAsync{TContext}"/> method.
/// Register on both pipelines explicitly:
/// <code>
/// emit.InboundPipeline.Use(typeof(LoggingMiddleware&lt;&gt;));
/// emit.OutboundPipeline.Use(typeof(LoggingMiddleware&lt;&gt;));
/// </code>
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
public abstract class MessageMiddleware<T> : IMiddleware<InboundContext<T>>, IMiddleware<OutboundContext<T>>
{
    /// <inheritdoc />
    Task IMiddleware<InboundContext<T>>.InvokeAsync(InboundContext<T> context, MessageDelegate<InboundContext<T>> next)
        => InvokeAsync(context, next);

    /// <inheritdoc />
    Task IMiddleware<OutboundContext<T>>.InvokeAsync(OutboundContext<T> context, MessageDelegate<OutboundContext<T>> next)
        => InvokeAsync(context, next);

    /// <summary>
    /// Processes a message and optionally delegates to the next middleware in the pipeline.
    /// Called for both inbound and outbound directions.
    /// </summary>
    /// <typeparam name="TContext">The concrete context type (inferred — do not specify).</typeparam>
    /// <param name="context">The typed message context flowing through the pipeline.</param>
    /// <param name="next">The next delegate in the pipeline.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected abstract Task InvokeAsync<TContext>(TContext context, MessageDelegate<TContext> next)
        where TContext : MessageContext<T>;
}

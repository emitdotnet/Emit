namespace Emit.Abstractions.Pipeline;

/// <summary>
/// Optional base class for cross-cutting middleware that runs on both consume and outbound pipelines.
/// Implements <see cref="IMiddleware{TContext}"/> for both <see cref="ConsumeContext{T}"/> and
/// <see cref="SendContext{T}"/>, dispatching to a single <see cref="InvokeAsync{TContext}"/> method.
/// Register on both pipelines via <see cref="IMessagePipelineBuilder"/>.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
public abstract class MessageMiddleware<T> : IMiddleware<ConsumeContext<T>>, IMiddleware<SendContext<T>>
{
    /// <inheritdoc />
    Task IMiddleware<ConsumeContext<T>>.InvokeAsync(ConsumeContext<T> context, IMiddlewarePipeline<ConsumeContext<T>> next)
        => InvokeAsync(context, next);

    /// <inheritdoc />
    Task IMiddleware<SendContext<T>>.InvokeAsync(SendContext<T> context, IMiddlewarePipeline<SendContext<T>> next)
        => InvokeAsync(context, next);

    /// <summary>
    /// Processes a message and optionally delegates to the next middleware in the pipeline.
    /// Called for both inbound and outbound directions.
    /// </summary>
    /// <typeparam name="TContext">The concrete context type (inferred — do not specify).</typeparam>
    /// <param name="context">The typed message context flowing through the pipeline.</param>
    /// <param name="next">The next pipeline in the chain.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected abstract Task InvokeAsync<TContext>(TContext context, IMiddlewarePipeline<TContext> next)
        where TContext : MessageContext<T>;
}

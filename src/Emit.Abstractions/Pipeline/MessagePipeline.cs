namespace Emit.Abstractions.Pipeline;

/// <summary>
/// Composes a list of typed middleware instances and a terminal pipeline into a single
/// <see cref="IMiddlewarePipeline{TContext}"/> chain. This is the core pipeline engine — a pure function
/// with no dependency injection or builder awareness.
/// </summary>
public static class MessagePipeline
{
    /// <summary>
    /// Builds an <see cref="IMiddlewarePipeline{TContext}"/> chain from middleware instances wrapping a terminal.
    /// Middleware executes in list order (index 0 = outermost, last = closest to terminal).
    /// </summary>
    /// <typeparam name="TContext">The pipeline context type.</typeparam>
    /// <param name="terminal">The final pipeline in the chain (e.g., handler invoker).</param>
    /// <param name="middleware">Middleware instances in execution order. Empty = returns terminal directly.</param>
    /// <returns>A composed pipeline that flows through all middleware before reaching the terminal.</returns>
    public static IMiddlewarePipeline<TContext> Build<TContext>(
        IMiddlewarePipeline<TContext> terminal,
        IReadOnlyList<IMiddleware<TContext>> middleware)
        where TContext : MessageContext
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(middleware);

        var next = terminal;

        for (var i = middleware.Count - 1; i >= 0; i--)
        {
            var current = middleware[i];
            var capturedNext = next;
            next = new MiddlewarePipelineNode<TContext>(current, capturedNext);
        }

        return next;
    }

    private sealed class MiddlewarePipelineNode<TContext>(
        IMiddleware<TContext> middleware,
        IMiddlewarePipeline<TContext> next) : IMiddlewarePipeline<TContext>
        where TContext : MessageContext
    {
        public Task InvokeAsync(TContext context) => middleware.InvokeAsync(context, next);
    }
}

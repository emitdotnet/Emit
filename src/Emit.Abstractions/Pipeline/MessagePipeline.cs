namespace Emit.Abstractions.Pipeline;

/// <summary>
/// Composes a list of typed middleware instances and a terminal delegate into a single
/// <see cref="MessageDelegate{TContext}"/> chain. This is the core pipeline engine — a pure function
/// with no dependency injection or builder awareness.
/// </summary>
public static class MessagePipeline
{
    /// <summary>
    /// Builds a <see cref="MessageDelegate{TContext}"/> chain from middleware instances wrapping a terminal.
    /// Middleware executes in list order (index 0 = outermost, last = closest to terminal).
    /// </summary>
    /// <typeparam name="TContext">The pipeline context type.</typeparam>
    /// <param name="terminal">The final delegate in the chain (e.g., handler invoker).</param>
    /// <param name="middleware">Middleware instances in execution order. Empty = returns terminal directly.</param>
    /// <returns>A composed delegate that flows through all middleware before reaching the terminal.</returns>
    public static MessageDelegate<TContext> Build<TContext>(
        MessageDelegate<TContext> terminal,
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
            next = context => current.InvokeAsync(context, capturedNext);
        }

        return next;
    }
}

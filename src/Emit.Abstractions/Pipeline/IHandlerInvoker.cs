namespace Emit.Abstractions.Pipeline;

/// <summary>
/// Terminal adapter that invokes a strongly-typed handler at the end of a middleware pipeline.
/// The core library provides a default implementation. Patterns with specialized needs supply their own.
/// </summary>
/// <typeparam name="TContext">The pipeline context type (e.g., <c>InboundContext&lt;TValue&gt;</c>).</typeparam>
public interface IHandlerInvoker<TContext> where TContext : MessageContext
{
    /// <summary>
    /// Invokes the handler for the given message context.
    /// </summary>
    /// <param name="context">The typed message context containing the message data and services.</param>
    /// <returns>A task representing the asynchronous handler invocation.</returns>
    Task InvokeAsync(TContext context);
}

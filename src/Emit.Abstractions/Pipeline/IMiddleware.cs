namespace Emit.Abstractions.Pipeline;

/// <summary>
/// A middleware component in the typed message processing pipeline.
/// The context type parameter determines the direction — inbound middleware implements
/// <c>IMiddleware&lt;InboundContext&lt;T&gt;&gt;</c>, outbound implements
/// <c>IMiddleware&lt;OutboundContext&lt;T&gt;&gt;</c>. Register open generic types
/// (e.g., <c>typeof(LoggingMiddleware&lt;&gt;)</c>) for global middleware that is closed
/// per message type at build time.
/// </summary>
/// <typeparam name="TContext">
/// The pipeline context type. Determines which pipelines this middleware can participate in.
/// </typeparam>
public interface IMiddleware<TContext> where TContext : MessageContext
{
    /// <summary>
    /// Processes a message and optionally delegates to the next middleware in the pipeline.
    /// </summary>
    /// <param name="context">The typed message context flowing through the pipeline.</param>
    /// <param name="next">The next delegate in the pipeline. Call to continue processing; skip to short-circuit.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task InvokeAsync(TContext context, MessageDelegate<TContext> next);
}

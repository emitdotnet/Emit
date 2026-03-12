namespace Emit.Abstractions.Pipeline;

/// <summary>
/// Terminal adapter that invokes a strongly-typed handler at the end of a middleware pipeline.
/// The core library provides a default implementation. Patterns with specialized needs supply their own.
/// </summary>
/// <typeparam name="TContext">The pipeline context type (e.g., <c>ConsumeContext&lt;TValue&gt;</c>).</typeparam>
public interface IHandlerInvoker<TContext> : IMiddlewarePipeline<TContext>
    where TContext : MessageContext
{
}

namespace Emit.Pipeline;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;

/// <summary>
/// Adapts an <see cref="IHandlerInvoker{TContext}"/> to the <see cref="IMiddlewarePipeline{TContext}"/>
/// interface so that terminal handler invokers can be used directly as pipeline terminals.
/// </summary>
/// <typeparam name="TContext">The pipeline context type.</typeparam>
public sealed class HandlerInvokerPipeline<TContext>(
    IHandlerInvoker<TContext> invoker) : IMiddlewarePipeline<TContext>
    where TContext : MessageContext
{
    /// <inheritdoc />
    public Task InvokeAsync(TContext context) => invoker.InvokeAsync(context);
}

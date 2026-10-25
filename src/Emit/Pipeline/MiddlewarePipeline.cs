namespace Emit.Pipeline;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;

/// <summary>
/// Bridges an <see cref="IMiddleware{TContext}"/> into an <see cref="IMiddlewarePipeline{TContext}"/>
/// by capturing the next pipeline reference.
/// </summary>
/// <typeparam name="TContext">The pipeline context type.</typeparam>
internal sealed class MiddlewarePipeline<TContext>(
    IMiddleware<TContext> middleware,
    IMiddlewarePipeline<TContext> next) : IMiddlewarePipeline<TContext>
    where TContext : MessageContext
{
    /// <inheritdoc />
    public Task InvokeAsync(TContext context) => middleware.InvokeAsync(context, () => next.InvokeAsync(context));
}

namespace Emit.Pipeline;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;

/// <summary>
/// No-op terminal pipeline that completes immediately. Used as the default terminal
/// when no further processing is needed.
/// </summary>
/// <typeparam name="TContext">The pipeline context type.</typeparam>
internal sealed class EmptyPipeline<TContext> : IMiddlewarePipeline<TContext>
    where TContext : MessageContext
{
    /// <summary>
    /// Shared singleton instance.
    /// </summary>
    public static readonly EmptyPipeline<TContext> Instance = new();

    /// <inheritdoc />
    public Task InvokeAsync(TContext context) => Task.CompletedTask;
}

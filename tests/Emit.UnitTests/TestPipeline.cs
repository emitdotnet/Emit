namespace Emit.UnitTests;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;

/// <summary>
/// Test helper that wraps a <see cref="Func{TContext, Task}"/> delegate into an
/// <see cref="IMiddlewarePipeline{TContext}"/> for use in unit tests.
/// </summary>
internal sealed class TestPipeline<TContext>(Func<TContext, Task> func) : IMiddlewarePipeline<TContext>
    where TContext : MessageContext
{
    public Task InvokeAsync(TContext context) => func(context);
}

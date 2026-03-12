namespace Emit.Pipeline;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;

/// <summary>
/// Internal middleware that gates the inbound pipeline based on a predicate.
/// When the predicate returns <c>false</c>, the pipeline is short-circuited and the
/// consumer handler is never invoked.
/// </summary>
internal sealed class ConsumerFilterMiddleware<TMessage>(
    Func<ConsumeContext<TMessage>, CancellationToken, ValueTask<bool>> predicate)
    : IMiddleware<ConsumeContext<TMessage>>
{
    /// <inheritdoc />
    public async Task InvokeAsync(ConsumeContext<TMessage> context, IMiddlewarePipeline<ConsumeContext<TMessage>> next)
    {
        if (await predicate(context, context.CancellationToken).ConfigureAwait(false))
            await next.InvokeAsync(context).ConfigureAwait(false);
    }
}

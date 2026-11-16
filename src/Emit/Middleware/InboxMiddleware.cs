namespace Emit.Middleware;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Middleware that records the message in the inbox before the consumer runs, in the transaction of its unit of
/// work, and skips the consumer when the message is already recorded. It sits directly inside the unit of work, so a
/// failed attempt rolls its record back with the consumer's own writes.
/// </summary>
/// <typeparam name="TValue">The message value type.</typeparam>
/// <param name="handler">Identifies the consumer in the inbox.</param>
internal sealed class InboxMiddleware<TValue>(string handler) : IMiddleware<ConsumeContext<TValue>>
{
    /// <inheritdoc />
    public async Task InvokeAsync(ConsumeContext<TValue> context, Func<Task> next)
    {
        var inbox = context.Services.GetRequiredService<IInboxRepository>();
        if (!await inbox.TryRecordAsync(handler, context.MessageId, context.CancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await next().ConfigureAwait(false);
    }
}

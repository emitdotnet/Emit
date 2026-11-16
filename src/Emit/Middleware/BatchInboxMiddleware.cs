namespace Emit.Middleware;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Middleware that records the items of a batch in the inbox in one call before the batch consumer runs, in the
/// transaction of its unit of work, and removes the items already recorded from the batch. The consumer is skipped
/// when none is left. A message id appearing twice in one batch keeps only its first item.
/// </summary>
/// <typeparam name="TItem">The item value type.</typeparam>
/// <param name="handler">Identifies the batch consumer in the inbox.</param>
internal sealed class BatchInboxMiddleware<TItem>(string handler) : IMiddleware<ConsumeContext<MessageBatch<TItem>>>
{
    /// <inheritdoc />
    public async Task InvokeAsync(ConsumeContext<MessageBatch<TItem>> context, Func<Task> next)
    {
        var batch = context.Message;
        var inbox = context.Services.GetRequiredService<IInboxRepository>();
        var recorded = await inbox
            .RecordAsync(handler, [.. batch.Select(item => item.TransportContext.MessageId)], context.CancellationToken)
            .ConfigureAwait(false);

        var taken = new HashSet<string>(StringComparer.Ordinal);
        var items = batch
            .Where(item => recorded.Contains(item.TransportContext.MessageId) && taken.Add(item.TransportContext.MessageId))
            .ToList();

        if (items.Count == 0)
        {
            return;
        }

        if (items.Count < batch.Count)
        {
            context.WithMessage(new MessageBatch<TItem>(items));
        }

        await next().ConfigureAwait(false);
    }
}

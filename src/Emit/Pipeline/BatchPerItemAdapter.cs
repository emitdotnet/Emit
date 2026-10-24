namespace Emit.Pipeline;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;

/// <summary>
/// Lifts an item-level <see cref="IMiddleware{TContext}"/> for <see cref="ConsumeContext{TItem}"/>
/// into a batch-level middleware for <see cref="ConsumeContext{T}"/> of <see cref="MessageBatch{TItem}"/>.
/// The wrapped inner middleware is invoked once per batch item against a per-item context derived
/// via <see cref="ConsumeContext{T}.FromBatchItem"/>.
/// <para>
/// Item passes when the inner middleware calls <c>next</c>. Item drops out when the inner middleware
/// short-circuits without calling <c>next</c>. Exceptions thrown by the inner middleware propagate
/// to the outer pipeline (the standard transient-error path) and unwind the batch. Before an exception
/// propagates, the items already dropped leave the batch, so a retry evaluates only the items not yet
/// dropped, and an error action applies only to them.
/// </para>
/// <para>
/// After all items are evaluated, the surviving items replace the batch via
/// <see cref="MessageContext.WithMessage"/>. When all items drop out, the pipeline short-circuits
/// without invoking the outer <c>next</c>.
/// </para>
/// </summary>
/// <typeparam name="TItem">The item message type (the inner type of the batch).</typeparam>
internal sealed class BatchPerItemAdapter<TItem>(
    Func<IServiceProvider, IMiddleware<ConsumeContext<TItem>>> innerFactory)
    : IMiddleware<ConsumeContext<MessageBatch<TItem>>>
{
    /// <inheritdoc />
    public async Task InvokeAsync(
        ConsumeContext<MessageBatch<TItem>> context,
        Func<Task> next)
    {
        var inner = innerFactory(context.Services);
        var originalBatch = context.Message;
        var survivors = new List<BatchItem<TItem>>(originalBatch.Count);
        var droppedCount = 0;

        for (var index = 0; index < originalBatch.Count; index++)
        {
            var item = originalBatch[index];
            var itemContext = ConsumeContext<TItem>.FromBatchItem(context, item);
            var reached = false;

            try
            {
                await inner.InvokeAsync(itemContext, () =>
                {
                    reached = true;
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            }
            catch when (droppedCount > 0)
            {
                // A dropped item was already dead-lettered or discarded: it must not be evaluated again
                context.WithMessage(new MessageBatch<TItem>([.. survivors, .. originalBatch.Skip(index)]));
                throw;
            }

            if (reached)
                survivors.Add(item);
            else
                droppedCount++;
        }

        // All items dropped → short-circuit without invoking the outer pipeline.
        if (survivors.Count == 0)
            return;

        // Some items dropped → replace batch in-place (preserves Features, Services, etc.).
        if (droppedCount > 0)
            context.WithMessage(new MessageBatch<TItem>(survivors));

        await next().ConfigureAwait(false);
    }
}

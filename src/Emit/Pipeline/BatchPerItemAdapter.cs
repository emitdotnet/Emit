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
/// to the outer pipeline (the standard transient-error path) and unwind the batch.
/// </para>
/// <para>
/// After all items are evaluated, the surviving items replace the batch via
/// <see cref="MessageContext{T}.WithMessage"/>. When all items drop out, the pipeline short-circuits
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
        IMiddlewarePipeline<ConsumeContext<MessageBatch<TItem>>> next)
    {
        var inner = innerFactory(context.Services);
        var originalBatch = context.Message;
        var survivors = new List<BatchItem<TItem>>(originalBatch.Count);
        var droppedCount = 0;

        foreach (var item in originalBatch)
        {
            var itemContext = ConsumeContext<TItem>.FromBatchItem(context, item);
            var sentinel = new SentinelTerminal<TItem>();

            await inner.InvokeAsync(itemContext, sentinel).ConfigureAwait(false);

            if (sentinel.Reached)
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

        await next.InvokeAsync(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Per-item terminal that records whether the inner middleware called <c>next</c>.
    /// </summary>
    private sealed class SentinelTerminal<T> : IMiddlewarePipeline<ConsumeContext<T>>
    {
        public bool Reached { get; private set; }

        public Task InvokeAsync(ConsumeContext<T> context)
        {
            Reached = true;
            return Task.CompletedTask;
        }
    }
}

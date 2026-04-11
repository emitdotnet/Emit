namespace Emit.Testing;

using System.Collections.Concurrent;
using Emit.Abstractions;

/// <summary>
/// Batch consumer handler that captures received batches for assertion.
/// Register as a singleton in the service collection and use
/// <c>AddBatchConsumer&lt;BatchSinkConsumer&lt;T&gt;&gt;()</c> on the
/// consumer group builder.
/// </summary>
/// <typeparam name="T">The message value type.</typeparam>
public sealed class BatchSinkConsumer<T> : IBatchConsumer<T>
{
    private readonly ConcurrentQueue<IReadOnlyList<T>> receivedBatches = new();

    /// <inheritdoc />
    public Task ConsumeAsync(ConsumeContext<MessageBatch<T>> context, CancellationToken cancellationToken)
    {
        var batch = context.Message;
        var messages = new List<T>(batch.Count);
        foreach (var item in batch)
        {
            messages.Add(item.Message);
        }

        receivedBatches.Enqueue(messages);
        return Task.CompletedTask;
    }

    /// <summary>
    /// All batches received so far, in order. Each entry is the list of messages
    /// from one batch pipeline invocation.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<T>> ReceivedBatches => [.. receivedBatches];

    /// <summary>
    /// All individual messages received across all batches, flattened and in order.
    /// </summary>
    public IReadOnlyList<T> Messages => receivedBatches.SelectMany(b => b).ToList();
}

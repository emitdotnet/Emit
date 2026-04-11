namespace Emit.Abstractions;

using System.Collections;

/// <summary>
/// An immutable batch of items that flows through the consume pipeline as the message
/// payload of <c>ConsumeContext&lt;MessageBatch&lt;T&gt;&gt;</c>. Implements
/// <see cref="IReadOnlyList{T}"/> for iteration and <see cref="IBatchMessage"/> for
/// framework-level batch detection.
/// </summary>
/// <typeparam name="T">The message value type.</typeparam>
public sealed class MessageBatch<T> : IReadOnlyList<BatchItem<T>>, IBatchMessage
{
    private readonly IReadOnlyList<BatchItem<T>> items;

    /// <summary>
    /// Creates a new batch from the given items.
    /// </summary>
    /// <param name="items">The batch items. Must not be null.</param>
    public MessageBatch(IReadOnlyList<BatchItem<T>> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        this.items = items;
    }

    /// <summary>
    /// The items in the batch.
    /// </summary>
    public IReadOnlyList<BatchItem<T>> Items => items;

    /// <inheritdoc />
    public int Count => items.Count;

    /// <inheritdoc />
    public BatchItem<T> this[int index] => items[index];

    /// <inheritdoc />
    public IEnumerator<BatchItem<T>> GetEnumerator() => items.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    IEnumerable<TransportContext> IBatchMessage.GetItemTransportContexts()
    {
        foreach (var item in items)
        {
            yield return item.TransportContext;
        }
    }
}

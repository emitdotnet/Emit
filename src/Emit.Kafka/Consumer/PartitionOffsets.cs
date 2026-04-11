namespace Emit.Kafka.Consumer;

/// <summary>
/// Per-partition offset tracking using the contiguous watermark algorithm.
/// Thread-safe via lock.
/// </summary>
internal sealed class PartitionOffsets
{
    private readonly LinkedList<long> receivedOrder = new();
    private readonly SortedSet<long> completedOutOfOrder = [];
    private readonly object syncLock = new();

    /// <summary>
    /// Registers a newly received offset. Called before dispatching to a worker.
    /// </summary>
    public void Enqueue(long offset)
    {
        lock (syncLock)
        {
            receivedOrder.AddLast(offset);
        }
    }

    /// <summary>
    /// Marks an offset as processed and attempts to advance the watermark.
    /// Returns the new watermark if it advanced, or null.
    /// </summary>
    public long? MarkAsProcessed(long offset)
    {
        lock (syncLock)
        {
            if (receivedOrder.First is null)
            {
                return null;
            }

            if (!TryCompleteOffset(offset))
            {
                return null;
            }

            return AdvanceWatermark(offset);
        }
    }

    /// <summary>
    /// Marks multiple offsets as processed in a single locked operation.
    /// Returns the new watermark if it advanced, or null.
    /// </summary>
    public long? MarkBatchAsProcessed(ReadOnlySpan<long> offsets)
    {
        lock (syncLock)
        {
            long? watermark = null;

            foreach (var offset in offsets)
            {
                if (TryCompleteOffset(offset))
                {
                    watermark = offset;
                }
            }

            return AdvanceWatermark(watermark);
        }
    }

    /// <summary>
    /// If the offset is the current head of the received queue, removes it and returns true.
    /// Otherwise, records it as completed out-of-order and returns false.
    /// Must be called under <see cref="syncLock"/>.
    /// </summary>
    private bool TryCompleteOffset(long offset)
    {
        if (receivedOrder.First is not null && receivedOrder.First.Value == offset)
        {
            receivedOrder.RemoveFirst();
            return true;
        }

        completedOutOfOrder.Add(offset);
        return false;
    }

    /// <summary>
    /// Advances the watermark past any contiguous completed offsets at the front of the queue.
    /// Must be called under <see cref="syncLock"/>.
    /// </summary>
    private long? AdvanceWatermark(long? watermark)
    {
        while (receivedOrder.First is not null && completedOutOfOrder.Remove(receivedOrder.First.Value))
        {
            watermark = receivedOrder.First.Value;
            receivedOrder.RemoveFirst();
        }

        return watermark;
    }
}

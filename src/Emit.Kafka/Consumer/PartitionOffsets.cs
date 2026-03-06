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

            if (receivedOrder.First.Value != offset)
            {
                completedOutOfOrder.Add(offset);
                return null;
            }

            receivedOrder.RemoveFirst();
            var watermark = offset;

            while (receivedOrder.First is not null && completedOutOfOrder.Remove(receivedOrder.First.Value))
            {
                watermark = receivedOrder.First.Value;
                receivedOrder.RemoveFirst();
            }

            return watermark;
        }
    }
}

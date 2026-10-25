namespace Emit.Kafka.Consumer;

using System.Collections.Concurrent;

/// <summary>
/// Holds a <see cref="PartitionOffsets"/> per assigned partition and routes
/// enqueue/completion calls to the correct partition tracker.
/// </summary>
internal class OffsetManager
{
    private readonly ConcurrentDictionary<TopicPartitionKey, PartitionOffsets> partitions = new();
    private readonly OffsetCommitter committer;
    private volatile bool retired;

    /// <summary>
    /// Creates a new offset manager.
    /// </summary>
    public OffsetManager(OffsetCommitter committer)
    {
        this.committer = committer;
    }

    /// <summary>
    /// Registers a received offset for a topic-partition. Called before dispatching.
    /// </summary>
    public virtual void Enqueue(string topic, int partition, long offset)
    {
        var key = new TopicPartitionKey(topic, partition);
        var tracker = partitions.GetOrAdd(key, _ => new PartitionOffsets());
        tracker.Enqueue(offset);
    }

    /// <summary>
    /// Returns the lowest offset of a topic-partition received and not yet processed, or <see langword="null"/>
    /// when there is none.
    /// </summary>
    public long? FirstUnhandled(string topic, int partition) =>
        partitions.TryGetValue(new TopicPartitionKey(topic, partition), out var tracker) ? tracker.FirstUnhandled : null;

    /// <summary>
    /// Marks an offset as processed. Forwards watermark advances to the committer.
    /// </summary>
    public virtual void MarkAsProcessed(string topic, int partition, long offset)
    {
        if (retired)
        {
            return;
        }

        var key = new TopicPartitionKey(topic, partition);
        if (!partitions.TryGetValue(key, out var tracker))
        {
            return;
        }

        var watermark = tracker.MarkAsProcessed(offset);
        if (watermark.HasValue)
        {
            committer.RecordCommittableOffset(topic, partition, watermark.Value);
        }
    }

    /// <summary>
    /// Marks multiple offsets as processed for a single topic-partition.
    /// Acquires the lock once per partition instead of per offset.
    /// </summary>
    public virtual void MarkBatchAsProcessed(string topic, int partition, ReadOnlySpan<long> offsets)
    {
        if (retired)
        {
            return;
        }

        var key = new TopicPartitionKey(topic, partition);
        if (!partitions.TryGetValue(key, out var tracker))
            return;

        var watermark = tracker.MarkBatchAsProcessed(offsets);
        if (watermark.HasValue)
        {
            committer.RecordCommittableOffset(topic, partition, watermark.Value);
        }
    }

    /// <summary>
    /// Stops this manager from recording completions, once the worker pool it served is replaced.
    /// </summary>
    public void Retire()
    {
        retired = true;
    }
}

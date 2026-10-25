namespace Emit.Kafka.Consumer;

using System.Collections.Concurrent;
using Emit.Kafka.Metrics;
using Emit.Kafka.Observability;
using Microsoft.Extensions.Logging;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Accumulates committable watermark offsets and periodically flushes them to Kafka.
/// </summary>
internal sealed class OffsetCommitter : IAsyncDisposable
{
    private ConcurrentDictionary<TopicPartitionKey, long> pendingCommits = new();
    private readonly Timer commitTimer;
    private readonly ConfluentKafka.IConsumer<byte[], byte[]> consumer;
    private readonly string groupId;
    private readonly KafkaConsumerObserverInvoker observerInvoker;
    private readonly KafkaMetrics kafkaMetrics;
    private readonly ILogger logger;

    /// <summary>
    /// Creates a new offset committer.
    /// </summary>
    public OffsetCommitter(
        ConfluentKafka.IConsumer<byte[], byte[]> consumer,
        TimeSpan commitInterval,
        string groupId,
        KafkaConsumerObserverInvoker observerInvoker,
        KafkaMetrics kafkaMetrics,
        ILogger logger)
    {
        this.consumer = consumer;
        this.groupId = groupId;
        this.observerInvoker = observerInvoker;
        this.kafkaMetrics = kafkaMetrics;
        this.logger = logger;
        commitTimer = new Timer(OnCommitTimer, null, commitInterval, commitInterval);
    }

    /// <summary>
    /// Records a new committable watermark offset for a topic-partition.
    /// </summary>
    public void RecordCommittableOffset(string topic, int partition, long offset)
    {
        pendingCommits[new TopicPartitionKey(topic, partition)] = offset;
    }

    /// <summary>
    /// Drops the offsets waiting to be committed for partitions the consumer lost: another consumer owns them now, and
    /// committing them could move its progress back.
    /// </summary>
    /// <param name="partitions">The partitions lost.</param>
    public void Discard(IEnumerable<ConfluentKafka.TopicPartition> partitions)
    {
        foreach (var partition in partitions)
        {
            pendingCommits.TryRemove(new TopicPartitionKey(partition.Topic, partition.Partition.Value), out _);
        }
    }

    /// <summary>
    /// Immediately flushes all pending offsets to Kafka.
    /// </summary>
    public void Flush()
    {
        OnCommitTimer(null);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return commitTimer.DisposeAsync();
    }

    private void OnCommitTimer(object? state)
    {
        var snapshot = Interlocked.Exchange(ref pendingCommits, new ConcurrentDictionary<TopicPartitionKey, long>());

        if (snapshot.IsEmpty)
        {
            return;
        }

        var offsets = new List<ConfluentKafka.TopicPartitionOffset>(snapshot.Count);
        foreach (var kvp in snapshot)
        {
            offsets.Add(new ConfluentKafka.TopicPartitionOffset(
                kvp.Key.Topic,
                new ConfluentKafka.Partition(kvp.Key.Partition),
                new ConfluentKafka.Offset(kvp.Value + 1)));
        }

        try
        {
            consumer.Commit(offsets);
            kafkaMetrics.RecordOffsetCommit(groupId, KafkaMetrics.ResultSuccess);
            observerInvoker.OnOffsetsCommittedAsync(new OffsetsCommittedEvent(groupId, offsets.Select(o => new TopicPartitionOffset(o.Topic, o.Partition.Value, o.Offset.Value)).ToList())).GetAwaiter().GetResult();
        }
        catch (ConfluentKafka.KafkaException ex)
        {
            kafkaMetrics.RecordOffsetCommit(groupId, KafkaMetrics.ResultError);
            observerInvoker.OnOffsetCommitErrorAsync(new OffsetCommitErrorEvent(groupId, offsets.Select(o => new TopicPartitionOffset(o.Topic, o.Partition.Value, o.Offset.Value)).ToList(), ex)).GetAwaiter().GetResult();

            if (ex.Error.Code == ConfluentKafka.ErrorCode.UnknownMemberId)
            {
                logger.LogWarning(ex, "Commit failed with UnknownMemberId, discarding offsets");
                return;
            }

            logger.LogWarning(ex, "Commit failed, re-queuing offsets");
            foreach (var kvp in snapshot)
            {
                pendingCommits.TryAdd(kvp.Key, kvp.Value);
            }
        }
    }
}

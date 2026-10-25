namespace Emit.Kafka.Consumer;

using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Keeps the worker pool of a consumer group declaration, and the offset manager tracking what it handles, in step
/// with the partitions the consumer is assigned. No message of a partition is handled by two pools at once, and none
/// the consumer keeps is skipped when a pool is replaced. Every member is called from the poll loop's thread, the
/// rebalance callbacks running within its consume.
/// </summary>
/// <typeparam name="TKey">The message key type.</typeparam>
/// <typeparam name="TValue">The message value type.</typeparam>
internal sealed class PartitionAssignmentHandler<TKey, TValue>
{
    /// <summary>
    /// The offset manager of the running pool, which the poll loop registers each message with.
    /// </summary>
    public OffsetManager OffsetManager { get; private set; }

    private readonly WorkerPoolSupervisor<TKey, TValue> supervisor;
    private readonly OffsetCommitter committer;
    private readonly CancellationToken executionToken;

    /// <summary>
    /// Creates the handler, with no pool running until partitions are assigned.
    /// </summary>
    /// <param name="supervisor">The supervisor of the worker pool.</param>
    /// <param name="committer">The committer of the consumer's offsets.</param>
    /// <param name="executionToken">The token the pools stop with when the host stops.</param>
    public PartitionAssignmentHandler(
        WorkerPoolSupervisor<TKey, TValue> supervisor,
        OffsetCommitter committer,
        CancellationToken executionToken)
    {
        this.supervisor = supervisor;
        this.committer = committer;
        this.executionToken = executionToken;
        OffsetManager = new OffsetManager(committer);
    }

    /// <summary>
    /// Starts a pool for the partitions assigned. An incremental (cooperative) assignment adds partitions to those
    /// the running pool already handles: they go to the same workers, and a second pool would handle the same keys
    /// at the same time.
    /// </summary>
    public void Assigned()
    {
        if (!supervisor.IsRunning && !executionToken.IsCancellationRequested)
        {
            supervisor.Start(OffsetManager, executionToken);
        }
    }

    /// <summary>
    /// Returns the worker of the running pool a message goes to, or <see langword="null"/> while no pool runs: once
    /// the host is stopping no pool starts, yet the consume that ran the assignment can still return a message.
    /// </summary>
    /// <param name="message">The message consumed.</param>
    public ConsumerWorker<TKey, TValue>? WorkerFor(ConfluentKafka.ConsumeResult<byte[], byte[]> message) =>
        supervisor is { Workers: { } workers, DistributionStrategy: { } strategy }
            ? workers[strategy.SelectWorker(message.Message.Key, message.Partition.Value)]
            : null;

    /// <summary>
    /// Stops the pool for a revoke. An incremental (cooperative) rebalance revokes only some partitions: the consumer
    /// keeps the others, which go on at once with a new pool.
    /// </summary>
    /// <param name="consumer">The consumer the partitions are revoked from.</param>
    /// <param name="revoked">The partitions revoked.</param>
    public Task RevokedAsync(
        ConfluentKafka.IConsumer<byte[], byte[]> consumer,
        IReadOnlySet<ConfluentKafka.TopicPartition> revoked) =>
        ReplacePoolAsync(consumer, [.. consumer.Assignment.Where(p => !revoked.Contains(p))]);

    /// <summary>
    /// Stops the pool for partitions lost. Another consumer already owns them: the handlers still running are
    /// cancelled at once, and the offsets waiting to be committed for them are dropped.
    /// </summary>
    /// <param name="consumer">The consumer that lost the partitions.</param>
    /// <param name="lost">The partitions lost.</param>
    public async Task LostAsync(
        ConfluentKafka.IConsumer<byte[], byte[]> consumer,
        IReadOnlyCollection<ConfluentKafka.TopicPartition> lost)
    {
        await StopPoolAsync(consumer, keptPartitions: [], cancelAtOnce: true).ConfigureAwait(false);
        committer.Discard(lost);
    }

    /// <summary>
    /// Replaces a faulted pool. The consumer keeps every partition.
    /// </summary>
    /// <param name="consumer">The consumer of the faulted pool.</param>
    public Task RestartAfterFaultAsync(ConfluentKafka.IConsumer<byte[], byte[]> consumer) =>
        ReplacePoolAsync(consumer, consumer.Assignment);

    /// <summary>
    /// Stops the pool, commits what it handled, and starts a new pool for the partitions the consumer keeps, from the
    /// messages the stopped pool left.
    /// </summary>
    private async Task ReplacePoolAsync(
        ConfluentKafka.IConsumer<byte[], byte[]> consumer,
        IReadOnlyCollection<ConfluentKafka.TopicPartition> keptPartitions)
    {
        await StopPoolAsync(consumer, keptPartitions, cancelAtOnce: false).ConfigureAwait(false);
        committer.Flush();

        if (keptPartitions.Count > 0 && !executionToken.IsCancellationRequested)
        {
            supervisor.Start(OffsetManager, executionToken);
        }
    }

    /// <summary>
    /// Stops the pool and gives the next one an offset manager of its own. Each partition the consumer keeps is
    /// rewound to its first message the stopped pool did not handle: its position is already past the messages left
    /// in the pool's queues, which would otherwise be skipped. A handler of the stopped pool that did not stop when
    /// cancelled keeps the retired manager, so what it finishes later cannot move the commits of a partition it no
    /// longer owns.
    /// </summary>
    private async Task StopPoolAsync(
        ConfluentKafka.IConsumer<byte[], byte[]> consumer,
        IEnumerable<ConfluentKafka.TopicPartition> keptPartitions,
        bool cancelAtOnce)
    {
        await supervisor.StopAsync(cancelAtOnce).ConfigureAwait(false);

        foreach (var partition in keptPartitions)
        {
            if (OffsetManager.FirstUnhandled(partition.Topic, partition.Partition.Value) is { } offset)
            {
                consumer.Seek(new ConfluentKafka.TopicPartitionOffset(partition, offset));
            }
        }

        OffsetManager.Retire();
        OffsetManager = new OffsetManager(committer);
    }
}

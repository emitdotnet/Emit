namespace Emit.Kafka.Observability;

/// <summary>
/// Event args for when a Kafka consumer group worker starts polling.
/// </summary>
/// <param name="GroupId">The consumer group ID.</param>
/// <param name="Topic">The topic being consumed.</param>
/// <param name="WorkerCount">The number of processing workers.</param>
public sealed record ConsumerStartedEvent(string GroupId, string Topic, int WorkerCount);

/// <summary>
/// Event args for when a Kafka consumer group worker stops.
/// </summary>
/// <param name="GroupId">The consumer group ID.</param>
/// <param name="Topic">The topic that was being consumed.</param>
public sealed record ConsumerStoppedEvent(string GroupId, string Topic);

/// <summary>
/// Event args for when a Kafka consumer worker faults and triggers a restart.
/// </summary>
/// <param name="GroupId">The consumer group ID.</param>
/// <param name="Topic">The topic being consumed.</param>
/// <param name="Exception">The exception that caused the fault.</param>
public sealed record ConsumerFaultedEvent(string GroupId, string Topic, Exception Exception);

/// <summary>
/// Event args for when partitions are assigned during a rebalance.
/// </summary>
/// <param name="GroupId">The consumer group ID.</param>
/// <param name="Topic">The topic being consumed.</param>
/// <param name="Partitions">The partition numbers that were assigned.</param>
public sealed record PartitionsAssignedEvent(string GroupId, string Topic, IReadOnlyList<int> Partitions);

/// <summary>
/// Event args for when partitions are gracefully revoked during a rebalance.
/// </summary>
/// <param name="GroupId">The consumer group ID.</param>
/// <param name="Topic">The topic being consumed.</param>
/// <param name="Partitions">The partition numbers that were revoked.</param>
public sealed record PartitionsRevokedEvent(string GroupId, string Topic, IReadOnlyList<int> Partitions);

/// <summary>
/// Event args for when partitions are lost (unclean partition loss).
/// </summary>
/// <param name="GroupId">The consumer group ID.</param>
/// <param name="Topic">The topic being consumed.</param>
/// <param name="Partitions">The partition numbers that were lost.</param>
public sealed record PartitionsLostEvent(string GroupId, string Topic, IReadOnlyList<int> Partitions);

/// <summary>
/// Event args for when offsets are successfully committed.
/// </summary>
/// <param name="GroupId">The consumer group ID.</param>
/// <param name="Offsets">The offsets that were committed.</param>
public sealed record OffsetsCommittedEvent(string GroupId, IReadOnlyList<TopicPartitionOffset> Offsets);

/// <summary>
/// Event args for when an offset commit fails.
/// </summary>
/// <param name="GroupId">The consumer group ID.</param>
/// <param name="Offsets">The offsets that failed to commit.</param>
/// <param name="Exception">The exception thrown by the commit operation.</param>
public sealed record OffsetCommitErrorEvent(string GroupId, IReadOnlyList<TopicPartitionOffset> Offsets, Exception Exception);

/// <summary>
/// Event args for when message deserialization fails before pipeline entry.
/// </summary>
/// <param name="GroupId">The consumer group ID.</param>
/// <param name="Topic">The topic the message was consumed from.</param>
/// <param name="Partition">The partition the message was consumed from.</param>
/// <param name="Offset">The offset of the message that failed deserialization.</param>
/// <param name="Exception">The deserialization exception.</param>
public sealed record DeserializationErrorEvent(string GroupId, string Topic, int Partition, long Offset, Exception Exception);

namespace Emit.Kafka.DependencyInjection;

using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Consumer-specific configuration overrides layered on top of shared client config.
/// </summary>
public sealed class KafkaConsumerConfig
{
    /// <summary>
    /// Action to take when there is no initial offset in offset store or the desired offset is out of range:
    /// 'smallest','earliest' - automatically reset the offset to the smallest offset,
    /// 'largest','latest' - automatically reset the offset to the largest offset,
    /// 'error' - trigger an error which is retrieved by consuming messages and checking 'message->err'.
    /// </summary>
    public ConfluentKafka.AutoOffsetReset? AutoOffsetReset { get; set; }

    /// <summary>
    /// Client group session and failure detection timeout.
    /// The consumer sends periodic heartbeats to indicate its liveness to the broker.
    /// If no heartbeats are received by the broker within this timeout,
    /// the broker will remove the consumer from the group and trigger a rebalance.
    /// </summary>
    public TimeSpan? SessionTimeout { get; set; }

    /// <summary>
    /// Group session keepalive heartbeat interval.
    /// </summary>
    public TimeSpan? HeartbeatInterval { get; set; }

    /// <summary>
    /// Maximum allowed time between calls to consume messages for high-level consumers.
    /// If this interval is exceeded the consumer is considered failed and the group will rebalance
    /// in order to reassign the partitions to another consumer group member.
    /// Warning: Offset commits may be not possible at this point.
    /// </summary>
    public TimeSpan? MaxPollInterval { get; set; }

    /// <summary>
    /// Minimum number of bytes the broker responds with.
    /// If <see cref="FetchWaitMax"/> expires the accumulated data will be sent to the client regardless of this setting.
    /// </summary>
    public int? FetchMinBytes { get; set; }

    /// <summary>
    /// Maximum amount of data the broker shall return for a Fetch request.
    /// Messages are fetched in batches by the consumer and if the first message batch in the first non-empty partition
    /// is larger than this value, then the message batch will still be returned to ensure the consumer can make progress.
    /// </summary>
    public int? FetchMaxBytes { get; set; }

    /// <summary>
    /// Maximum time the broker may wait to fill the Fetch response with <see cref="FetchMinBytes"/> of messages.
    /// </summary>
    public TimeSpan? FetchWaitMax { get; set; }

    /// <summary>
    /// Initial maximum number of bytes per topic+partition to request when fetching messages from the broker.
    /// If the client encounters a message larger than this value it will gradually try to increase it
    /// until the entire message can be fetched.
    /// </summary>
    public int? MaxPartitionFetchBytes { get; set; }

    /// <summary>
    /// Enable static group membership. Static group members are able to leave and rejoin a group
    /// within the configured session timeout without prompting a group rebalance.
    /// This should be used in combination with a larger session timeout to avoid group rebalances
    /// caused by transient unavailability (e.g. process restarts). Requires broker version >= 2.3.0.
    /// </summary>
    public string? GroupInstanceId { get; set; }

    /// <summary>
    /// The name of one or more partition assignment strategies.
    /// The elected group leader will use a strategy supported by all members of the group
    /// to assign partitions to group members.
    /// Available strategies: range, roundrobin, cooperative-sticky.
    /// </summary>
    public ConfluentKafka.PartitionAssignmentStrategy? PartitionAssignmentStrategy { get; set; }

    /// <summary>
    /// Controls how to read messages written transactionally:
    /// <c>ReadCommitted</c> - only return transactional messages which have been committed.
    /// <c>ReadUncommitted</c> - return all messages, even transactional messages which have been aborted.
    /// </summary>
    public ConfluentKafka.IsolationLevel? IsolationLevel { get; set; }

    /// <summary>
    /// Verify CRC32 of consumed messages, ensuring no on-the-wire or on-disk corruption to the messages occurred.
    /// This check comes at slightly increased CPU usage.
    /// </summary>
    public bool? CheckCrcs { get; set; }

    /// <summary>
    /// Group protocol to use. Set to <see cref="ConfluentKafka.GroupProtocol.Consumer"/> to use the new
    /// KIP-848 consumer group protocol, or <see cref="ConfluentKafka.GroupProtocol.Classic"/> for the
    /// traditional protocol. Requires broker version >= 4.0 for the consumer protocol.
    /// </summary>
    public ConfluentKafka.GroupProtocol? GroupProtocol { get; set; }

    /// <summary>
    /// Applies non-null overrides onto a <see cref="ConfluentKafka.ConsumerConfig"/>.
    /// </summary>
    internal void ApplyTo(ConfluentKafka.ConsumerConfig config)
    {
        if (AutoOffsetReset.HasValue) config.AutoOffsetReset = AutoOffsetReset.Value;
        if (SessionTimeout.HasValue) config.SessionTimeoutMs = (int)SessionTimeout.Value.TotalMilliseconds;
        if (HeartbeatInterval.HasValue) config.HeartbeatIntervalMs = (int)HeartbeatInterval.Value.TotalMilliseconds;
        if (MaxPollInterval.HasValue) config.MaxPollIntervalMs = (int)MaxPollInterval.Value.TotalMilliseconds;
        if (FetchMinBytes.HasValue) config.FetchMinBytes = FetchMinBytes.Value;
        if (FetchMaxBytes.HasValue) config.FetchMaxBytes = FetchMaxBytes.Value;
        if (FetchWaitMax.HasValue) config.FetchWaitMaxMs = (int)FetchWaitMax.Value.TotalMilliseconds;
        if (MaxPartitionFetchBytes.HasValue) config.MaxPartitionFetchBytes = MaxPartitionFetchBytes.Value;
        if (GroupInstanceId is not null) config.GroupInstanceId = GroupInstanceId;
        if (PartitionAssignmentStrategy.HasValue) config.PartitionAssignmentStrategy = PartitionAssignmentStrategy.Value;
        if (IsolationLevel.HasValue) config.IsolationLevel = IsolationLevel.Value;
        if (CheckCrcs.HasValue) config.CheckCrcs = CheckCrcs.Value;
        if (GroupProtocol.HasValue) config.GroupProtocol = GroupProtocol.Value;
    }
}

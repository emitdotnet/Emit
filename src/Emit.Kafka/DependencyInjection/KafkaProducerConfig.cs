namespace Emit.Kafka.DependencyInjection;

using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Producer-specific configuration overrides layered on top of shared client config.
/// </summary>
public sealed class KafkaProducerConfig
{
    /// <summary>
    /// The number of acknowledgements the leader broker must receive from ISR brokers before responding to the request:
    /// Zero=Broker does not send any response/ack to client,
    /// One=The leader will write the record to its local log but will respond without awaiting full acknowledgement from all followers.
    /// All=Broker will block until message is committed by all in sync replicas (ISRs).
    /// </summary>
    public ConfluentKafka.Acks? Acks { get; set; }

    /// <summary>
    /// Delay to wait for messages in the producer queue to accumulate before constructing message batches to transmit to brokers.
    /// A higher value allows larger and more effective (less overhead, improved compression) batches of messages to accumulate
    /// at the expense of increased message delivery latency.
    /// </summary>
    public TimeSpan? Linger { get; set; }

    /// <summary>
    /// Maximum size (in bytes) of all messages batched in one MessageSet, including protocol framing overhead.
    /// This limit is applied after the first message has been added to the batch, regardless of the first message's size,
    /// this is to ensure that messages that exceed batch size are produced.
    /// The total MessageSet size is also limited by <see cref="BatchNumMessages"/> and message max bytes.
    /// </summary>
    public int? BatchSize { get; set; }

    /// <summary>
    /// When set to <c>true</c>, the producer will ensure that messages are successfully produced exactly once
    /// and in the original produce order.
    /// The following configuration properties are adjusted automatically (if not modified by the user) when idempotence is enabled:
    /// max.in.flight.requests.per.connection=5, retries=INT32_MAX, acks=all, queuing.strategy=fifo.
    /// Producer instantiation will fail if user-supplied configuration is incompatible.
    /// </summary>
    public bool? EnableIdempotence { get; set; }

    /// <summary>
    /// Compression codec to use for compressing message sets.
    /// This is the default value for all topics, may be overridden by the topic configuration property <c>compression.codec</c>.
    /// </summary>
    public ConfluentKafka.CompressionType? CompressionType { get; set; }

    /// <summary>
    /// Maximum number of messages batched in one MessageSet.
    /// The total MessageSet size is also limited by <see cref="BatchSize"/> and message max bytes.
    /// </summary>
    public int? BatchNumMessages { get; set; }

    /// <summary>
    /// Applies non-null overrides onto a <see cref="ConfluentKafka.ProducerConfig"/>.
    /// </summary>
    internal void ApplyTo(ConfluentKafka.ProducerConfig config)
    {
        if (Acks.HasValue) config.Acks = Acks.Value;
        if (Linger.HasValue) config.LingerMs = (int)Linger.Value.TotalMilliseconds;
        if (BatchSize.HasValue) config.BatchSize = BatchSize.Value;
        if (EnableIdempotence.HasValue) config.EnableIdempotence = EnableIdempotence.Value;
        if (CompressionType.HasValue) config.CompressionType = CompressionType.Value;
        if (BatchNumMessages.HasValue) config.BatchNumMessages = BatchNumMessages.Value;
    }
}

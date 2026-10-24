namespace Emit.Kafka.Consumer;

using Emit.Abstractions;

/// <summary>
/// Kafka-specific transport context. Extends <see cref="TransportContext"/> with
/// Kafka partition assignment metadata.
/// </summary>
public class KafkaTransportContext : TransportContext
{
    /// <summary>
    /// The Kafka topic this message was consumed from.
    /// </summary>
    public required string Topic { get; init; }

    /// <summary>
    /// The partition number within the topic.
    /// </summary>
    public required int Partition { get; init; }

    /// <summary>
    /// The offset of this message within the partition.
    /// </summary>
    public required long Offset { get; init; }

    /// <summary>
    /// The consumer group ID that received this message.
    /// </summary>
    public required string GroupId { get; init; }
}

/// <summary>
/// Generic Kafka transport context that carries the deserialized message key.
/// Consumers retrieve the key by casting <see cref="TransportContext"/> to this type
/// or by using the <c>GetKey</c> extension method.
/// </summary>
/// <typeparam name="TKey">The message key type.</typeparam>
public sealed class KafkaTransportContext<TKey> : KafkaTransportContext
{
    /// <summary>
    /// The deserialized message key.
    /// </summary>
    public required TKey Key { get; init; }
}

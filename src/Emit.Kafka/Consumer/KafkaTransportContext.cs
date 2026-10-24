namespace Emit.Kafka.Consumer;

using Emit.Abstractions;
using Emit.Kafka.Tracing;

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

    /// <summary>
    /// Whether the message is a tombstone: it has a key and no value.
    /// </summary>
    public bool IsTombstone { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// The consumer group, the partition and offset of a single message (a batch has neither), and whether the
    /// message is a tombstone.
    /// </remarks>
    public override IEnumerable<KeyValuePair<string, object?>> GetTracingTags()
    {
        if (Partition >= 0)
        {
            yield return new(KafkaActivityTagNames.DestinationPartitionId, Partition.ToString());
        }

        if (Offset >= 0)
        {
            yield return new(KafkaActivityTagNames.Offset, Offset);
        }

        yield return new(KafkaActivityTagNames.ConsumerGroupName, GroupId);

        if (IsTombstone)
        {
            yield return new(KafkaActivityTagNames.MessageTombstone, true);
        }
    }
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

    /// <inheritdoc />
    /// <remarks>
    /// Also the key, when it is a string: a key of another type has no canonical string form to report.
    /// </remarks>
    public override IEnumerable<KeyValuePair<string, object?>> GetTracingTags() =>
        Key is string key ? base.GetTracingTags().Append(new(KafkaActivityTagNames.MessageKey, key)) : base.GetTracingTags();
}

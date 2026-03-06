namespace Emit.Kafka.Consumer;

using Emit.Abstractions;

/// <summary>
/// Provides access to Kafka-specific message metadata through the feature collection.
/// Extends <see cref="IMessageSourceFeature"/> to provide typed access to topic, partition,
/// and offset while remaining compatible with transport-agnostic middleware.
/// </summary>
public interface IKafkaFeature : IMessageSourceFeature
{
    /// <summary>The Kafka topic name.</summary>
    string Topic { get; }

    /// <summary>The partition number.</summary>
    int Partition { get; }

    /// <summary>The message offset within the partition.</summary>
    long Offset { get; }
}

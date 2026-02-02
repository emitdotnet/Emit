namespace Emit.Provider.Kafka.Serialization;

using MessagePack;

/// <summary>
/// Represents the serialized Kafka message payload stored in the outbox.
/// </summary>
/// <remarks>
/// This class is serialized using MessagePack and stored in the <see cref="Emit.Models.OutboxEntry.Payload"/>
/// field. Only the Kafka provider knows how to deserialize and interpret this structure.
/// </remarks>
[MessagePackObject(AllowPrivate = true)]
internal sealed class KafkaPayload
{
    /// <summary>
    /// Gets or sets the target Kafka topic.
    /// </summary>
    [Key(0)]
    public required string Topic { get; set; }

    /// <summary>
    /// Gets or sets the serialized message key bytes.
    /// </summary>
    /// <remarks>
    /// Null if the message has no key.
    /// </remarks>
    [Key(1)]
    public byte[]? KeyBytes { get; set; }

    /// <summary>
    /// Gets or sets the serialized message value bytes.
    /// </summary>
    /// <remarks>
    /// Null if the message has a null value (tombstone).
    /// </remarks>
    [Key(2)]
    public byte[]? ValueBytes { get; set; }

    /// <summary>
    /// Gets or sets the message headers.
    /// </summary>
    /// <remarks>
    /// Null if the message has no headers.
    /// </remarks>
    [Key(3)]
    public Dictionary<string, byte[]>? Headers { get; set; }

    /// <summary>
    /// Gets or sets the target partition.
    /// </summary>
    /// <remarks>
    /// Null if partition should be determined by the partitioner.
    /// </remarks>
    [Key(4)]
    public int? Partition { get; set; }

    /// <summary>
    /// Gets or sets the message timestamp in Unix milliseconds.
    /// </summary>
    /// <remarks>
    /// Null if the broker should assign the timestamp.
    /// </remarks>
    [Key(5)]
    public long? TimestampUnixMs { get; set; }

    /// <summary>
    /// Gets or sets the timestamp type.
    /// </summary>
    /// <remarks>
    /// 0 = NotAvailable, 1 = CreateTime, 2 = LogAppendTime.
    /// </remarks>
    [Key(6)]
    public int TimestampType { get; set; }
}

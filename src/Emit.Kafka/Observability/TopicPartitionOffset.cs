namespace Emit.Kafka.Observability;

/// <summary>
/// Represents a topic, partition, and offset triple for observer event args.
/// </summary>
/// <param name="Topic">The Kafka topic name.</param>
/// <param name="Partition">The partition number.</param>
/// <param name="Offset">The offset within the partition.</param>
public sealed record TopicPartitionOffset(string Topic, int Partition, long Offset);

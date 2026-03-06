namespace Emit.Kafka.Consumer;

/// <summary>
/// Composite key for identifying a Kafka topic-partition pair.
/// </summary>
internal readonly record struct TopicPartitionKey(string Topic, int Partition);

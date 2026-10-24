namespace Emit.Kafka.Tracing;

/// <summary>
/// The OpenTelemetry semantic convention attributes the Kafka provider sets on Emit's spans.
/// </summary>
internal static class KafkaActivityTagNames
{
    public const string ConsumerGroupName = "messaging.consumer.group.name";
    public const string DestinationPartitionId = "messaging.destination.partition.id";
    public const string Offset = "messaging.kafka.offset";
    public const string MessageKey = "messaging.kafka.message.key";
    public const string MessageTombstone = "messaging.kafka.message.tombstone";
}

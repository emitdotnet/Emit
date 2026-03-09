namespace Emit.Kafka.Consumer;

using Emit.Abstractions;

/// <summary>
/// Kafka-specific dead-letter header constants built from the shared
/// <see cref="DeadLetterHeaders.SourcePrefix"/> and Kafka source property names.
/// </summary>
internal static class KafkaDeadLetterHeaders
{
    /// <summary>The source topic the message was consumed from.</summary>
    public const string SourceTopic = $"{DeadLetterHeaders.SourcePrefix}topic";

    /// <summary>The source partition the message was consumed from.</summary>
    public const string SourcePartition = $"{DeadLetterHeaders.SourcePrefix}partition";

    /// <summary>The source offset of the message within the partition.</summary>
    public const string SourceOffset = $"{DeadLetterHeaders.SourcePrefix}offset";
}

namespace Emit.Kafka.DependencyInjection;

/// <summary>
/// Carries the dead letter topic configuration registered by <see cref="KafkaBuilder.DeadLetter"/>.
/// </summary>
internal sealed class KafkaDeadLetterOptions
{
    /// <summary>
    /// The Kafka topic name where dead-lettered messages are produced.
    /// </summary>
    public required string TopicName { get; init; }

    /// <summary>
    /// The transport-agnostic destination address for tracing and diagnostics.
    /// </summary>
    public required Uri DestinationAddress { get; init; }
}

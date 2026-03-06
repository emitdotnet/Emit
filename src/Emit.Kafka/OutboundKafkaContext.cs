namespace Emit.Kafka;

using Emit.Abstractions;

/// <summary>
/// Outbound pipeline context carrying Kafka message data for production.
/// Created by <see cref="KafkaPipelineProducer{TKey, TValue}"/> before invoking
/// the outbound middleware pipeline.
/// </summary>
/// <typeparam name="TKey">The message key type.</typeparam>
/// <typeparam name="TValue">The message value type.</typeparam>
internal sealed class OutboundKafkaContext<TKey, TValue> : OutboundContext<TValue>
{
    /// <summary>The message key.</summary>
    public required TKey Key { get; init; }

    /// <summary>The Kafka topic name.</summary>
    public required string Topic { get; init; }

    /// <summary>Message headers as string key-value pairs.</summary>
    public required IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; }
}

namespace Emit.Kafka.Consumer;

using Emit.Abstractions;

/// <summary>
/// Inbound pipeline context carrying deserialized Kafka message data.
/// Inherits <see cref="InboundContext{T}"/> where T is the value type, providing typed
/// <see cref="MessageContext{T}.Message"/> access. The key is available via
/// <see cref="IKeyFeature{TKey}"/> on <see cref="MessageContext.Features"/>.
/// </summary>
/// <typeparam name="TKey">The message key type.</typeparam>
/// <typeparam name="TValue">The message value type.</typeparam>
internal sealed class InboundKafkaContext<TKey, TValue> : InboundContext<TValue>
{
    /// <summary>The deserialized message key.</summary>
    public required TKey Key { get; init; }

    /// <summary>The Kafka topic name.</summary>
    public required string Topic { get; init; }

    /// <summary>The partition number.</summary>
    public required int Partition { get; init; }

    /// <summary>The message offset within the partition.</summary>
    public required long Offset { get; init; }

    /// <summary>Message headers as string key-value pairs.</summary>
    public required IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; }
}

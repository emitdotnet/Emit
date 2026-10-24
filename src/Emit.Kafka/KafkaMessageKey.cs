namespace Emit.Kafka;

/// <summary>
/// The key of a Kafka message being produced, set by the producer as a payload of its send context. Producer
/// middleware reads it with <c>context.TryGetPayload&lt;KafkaMessageKey&lt;TKey&gt;&gt;()</c>.
/// </summary>
/// <typeparam name="TKey">The message key type.</typeparam>
/// <param name="Key">The message key.</param>
public sealed record KafkaMessageKey<TKey>(TKey Key);

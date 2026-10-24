namespace Emit.Kafka;

/// <summary>
/// What the Kafka producer passes along with a message it produces, as a payload of its send context: the key for
/// the terminal, and what the message's spans report about it.
/// </summary>
/// <param name="Key">The message key.</param>
/// <param name="IsTombstone">Whether the message has a key and no value.</param>
internal sealed record KafkaProducePayload(object? Key, bool IsTombstone);

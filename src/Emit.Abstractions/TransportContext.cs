namespace Emit.Abstractions;

/// <summary>
/// Pre-deserialization transport context. Carries raw message data, headers, and
/// transport-agnostic metadata. Provider-specific subclasses add transport-specific
/// properties (e.g., Kafka topic, partition, offset).
/// </summary>
public abstract class TransportContext : MessageContext
{
    /// <summary>
    /// Raw serialized key bytes, or <c>null</c> if the message has no key.
    /// </summary>
    public required byte[]? RawKey { get; init; }

    /// <summary>
    /// Raw serialized value bytes, or <c>null</c> if the message has no value.
    /// </summary>
    public required byte[]? RawValue { get; init; }

    /// <summary>
    /// Message headers as key-value pairs.
    /// </summary>
    public required IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; }

    /// <summary>
    /// Identifies the transport provider (e.g., "kafka").
    /// </summary>
    public required string ProviderId { get; init; }
}

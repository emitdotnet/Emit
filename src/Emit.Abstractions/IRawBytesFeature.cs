namespace Emit.Abstractions;

/// <summary>
/// Provides access to the raw binary key and value of a message as received from the transport.
/// Present when the transport delivers messages as byte arrays (e.g., Kafka).
/// Middleware can use this to forward messages to a dead letter destination without re-serialization.
/// </summary>
public interface IRawBytesFeature
{
    /// <summary>
    /// The raw message key bytes as received from the transport, or <c>null</c> if no key was present.
    /// </summary>
    byte[]? RawKey { get; }

    /// <summary>
    /// The raw message value bytes as received from the transport, or <c>null</c> if no value was present.
    /// </summary>
    byte[]? RawValue { get; }
}

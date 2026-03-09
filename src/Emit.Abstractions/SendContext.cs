namespace Emit.Abstractions;

/// <summary>
/// Outbound context for producing messages. Carries the typed message, a generated
/// message ID, timestamp, and mutable headers that middleware can populate before
/// the message reaches the transport.
/// </summary>
/// <typeparam name="T">The outbound message type.</typeparam>
public class SendContext<T> : MessageContext<T>
{
    /// <summary>
    /// Mutable headers that middleware can populate before the message is sent.
    /// </summary>
    public List<KeyValuePair<string, string>> Headers { get; init; } = [];
}

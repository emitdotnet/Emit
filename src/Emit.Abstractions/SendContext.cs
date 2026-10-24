namespace Emit.Abstractions;

/// <summary>
/// Context of a message being produced: its generated message ID, timestamp, and the headers middleware can
/// populate before the message reaches the transport. Middleware written for <see cref="SendContext"/> runs on
/// every producer, whatever its message type.
/// </summary>
public abstract class SendContext : MessageContext
{
    /// <summary>
    /// Mutable headers that middleware can populate before the message is sent.
    /// </summary>
    public List<KeyValuePair<string, string>> Headers { get; init; } = [];
}

/// <summary>
/// Context of a message of type <typeparamref name="T"/> being produced.
/// </summary>
/// <typeparam name="T">The outbound message type.</typeparam>
public class SendContext<T> : SendContext
{
    /// <summary>
    /// The message being produced.
    /// </summary>
    public new required T Message
    {
        get => base.Message is T message ? message : default!;
        init => base.Message = value;
    }
}

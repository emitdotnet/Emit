namespace Emit.Abstractions;

/// <summary>
/// The exception an outbox delivery throws when this node could not reach the message broker, as opposed to the
/// broker refusing the entry. The outbox daemon then gives up its run, so that once this node stops offering the
/// daemon another node, which may reach the broker, delivers the outbox.
/// </summary>
public sealed class MessageBrokerUnreachableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MessageBrokerUnreachableException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The error the message broker's client reported.</param>
    public MessageBrokerUnreachableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

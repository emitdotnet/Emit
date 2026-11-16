namespace Emit.Abstractions;

/// <summary>
/// Implemented by builders that configure one consumer: a consumer of a consumer group or a route of a router.
/// </summary>
/// <typeparam name="TMessage">The message type the consumer handles.</typeparam>
public interface IConsumerConfigurable<TMessage> : IInboundConfigurable<TMessage>
{
    /// <summary>
    /// Skips the messages the consumer already handled. Each message is recorded in the consumer's transaction, so the
    /// consumer must be decorated with <see cref="TransactionalAttribute"/>, and the persistence provider must enable
    /// the inbox.
    /// </summary>
    /// <remarks>
    /// The name of the consumer's type is part of its identity in the inbox, so renaming the type starts it over with no
    /// records. A copy that arrives after its record expired is handled again.
    /// </remarks>
    /// <returns>This builder for chaining.</returns>
    IConsumerConfigurable<TMessage> UseInbox();
}

namespace Emit.Abstractions.Pipeline;

using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;

/// <summary>
/// Extends inbound pipeline configuration with consumer-group-level features.
/// Error handling and validation configured here apply uniformly to all consumers
/// within the group. Methods return <see cref="IConsumerGroupConfigurable{TMessage}"/> so that
/// group-level methods can be chained in any order. The chain narrows to
/// <see cref="IInboundConfigurable{TMessage}"/> when <c>Use</c> or <c>Filter</c> is called.
/// </summary>
/// <typeparam name="TMessage">The message type processed by consumers in this group.</typeparam>
public interface IConsumerGroupConfigurable<TMessage> : IInboundConfigurable<TMessage>
{
    /// <summary>
    /// Configures error handling for all consumers in this group. When an exception
    /// occurs during message processing, the configured policy determines whether to
    /// retry, dead-letter, or discard the message.
    /// </summary>
    /// <param name="configure">Configures the error policy.</param>
    /// <returns>This builder for continued group-level or pipeline chaining.</returns>
    /// <exception cref="InvalidOperationException">OnError has already been called.</exception>
    IConsumerGroupConfigurable<TMessage> OnError(Action<ErrorPolicyBuilder> configure);

    /// <summary>
    /// Registers a class-based message validator that is resolved from the service provider
    /// for each message. The validator runs before every handler in this group and must
    /// produce a <see cref="MessageValidationResult"/>. A terminal action
    /// (<see cref="ErrorActionBuilder.DeadLetter()"/> or <see cref="ErrorActionBuilder.Discard"/>)
    /// must be configured for invalid messages.
    /// </summary>
    /// <typeparam name="TValidator">
    /// The validator type. Must implement <see cref="IMessageValidator{TMessage}"/>.
    /// </typeparam>
    /// <param name="configureAction">Configures the terminal action for validation failures.</param>
    /// <returns>This builder for continued group-level or pipeline chaining.</returns>
    /// <exception cref="InvalidOperationException">Validate has already been called.</exception>
    IConsumerGroupConfigurable<TMessage> Validate<TValidator>(Action<ErrorActionBuilder> configureAction)
        where TValidator : class, IMessageValidator<TMessage>;

    /// <summary>
    /// Registers an inline async delegate validator that runs before every handler
    /// in this group. A terminal action (<see cref="ErrorActionBuilder.DeadLetter()"/>
    /// or <see cref="ErrorActionBuilder.Discard"/>) must be configured for invalid messages.
    /// </summary>
    /// <param name="validator">The async validation delegate.</param>
    /// <param name="configureAction">Configures the terminal action for validation failures.</param>
    /// <returns>This builder for continued group-level or pipeline chaining.</returns>
    /// <exception cref="InvalidOperationException">Validate has already been called.</exception>
    IConsumerGroupConfigurable<TMessage> Validate(
        Func<TMessage, CancellationToken, Task<MessageValidationResult>> validator,
        Action<ErrorActionBuilder> configureAction);

    /// <summary>
    /// Registers an inline synchronous delegate validator that runs before every handler
    /// in this group. A terminal action (<see cref="ErrorActionBuilder.DeadLetter()"/>
    /// or <see cref="ErrorActionBuilder.Discard"/>) must be configured for invalid messages.
    /// </summary>
    /// <param name="validator">The synchronous validation delegate.</param>
    /// <param name="configureAction">Configures the terminal action for validation failures.</param>
    /// <returns>This builder for continued group-level or pipeline chaining.</returns>
    /// <exception cref="InvalidOperationException">Validate has already been called.</exception>
    IConsumerGroupConfigurable<TMessage> Validate(
        Func<TMessage, MessageValidationResult> validator,
        Action<ErrorActionBuilder> configureAction);
}

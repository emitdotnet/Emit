namespace Emit.FluentValidation;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;

/// <summary>
/// Extension methods for integrating FluentValidation validators into the Emit consumer pipeline.
/// </summary>
public static class FluentValidationExtensions
{
    /// <summary>
    /// Registers a FluentValidation-based message validator for this consumer group.
    /// The validator is resolved from DI as <c>IValidator&lt;TMessage&gt;</c> for each message.
    /// Validation failures are handled by the configured <paramref name="configureAction"/>.
    /// </summary>
    /// <typeparam name="TMessage">The message type processed by consumers in this group.</typeparam>
    /// <param name="builder">The consumer group builder.</param>
    /// <param name="configureAction">
    /// Configures the terminal action for validation failures (e.g., <c>a =&gt; a.DeadLetter()</c>
    /// or <c>a =&gt; a.Discard()</c>).
    /// </param>
    /// <returns>The builder for continued chaining.</returns>
    public static IConsumerGroupConfigurable<TMessage> ValidateWithFluentValidation<TMessage>(
        this IConsumerGroupConfigurable<TMessage> builder,
        Action<ErrorActionBuilder> configureAction)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureAction);

        return builder.Validate<FluentValidationMessageValidator<TMessage>>(configureAction);
    }
}

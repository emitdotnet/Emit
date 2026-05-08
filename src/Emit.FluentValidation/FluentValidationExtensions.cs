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
    /// <param name="allowNullMessages">
    /// When <see langword="true"/>, messages with a <see langword="null"/> payload are treated as
    /// valid and passed through without invoking the FluentValidation validator. When
    /// <see langword="false"/> (the default), null payloads produce a deterministic validation
    /// failure. Use <c>.SkipNullPayloads()</c> upstream if you prefer to silently drop nulls
    /// instead of routing them to the validation error action.
    /// </param>
    /// <returns>The builder for continued chaining.</returns>
    public static IConsumerGroupConfigurable<TMessage> ValidateWithFluentValidation<TMessage>(
        this IConsumerGroupConfigurable<TMessage> builder,
        Action<ErrorActionBuilder> configureAction,
        bool allowNullMessages = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureAction);

        return allowNullMessages
            ? builder.Validate<FluentValidationNullTolerantMessageValidator<TMessage>>(configureAction)
            : builder.Validate<FluentValidationMessageValidator<TMessage>>(configureAction);
    }
}

namespace Emit.FluentValidation;

using Emit.Abstractions;
using global::FluentValidation;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Bridges FluentValidation's <see cref="IValidator{T}"/> into Emit's
/// <see cref="IMessageValidator{TValue}"/> pipeline. Resolved from DI per message.
/// Treats <see langword="null"/> messages as a deterministic validation failure;
/// override <see cref="OnNullMessage"/> to change that behavior.
/// </summary>
/// <typeparam name="TMessage">The message type to validate.</typeparam>
internal class FluentValidationMessageValidator<TMessage>(
    IServiceProvider services) : IMessageValidator<TMessage>
{
    /// <inheritdoc />
    public async Task<MessageValidationResult> ValidateAsync(
        TMessage message, CancellationToken cancellationToken)
    {
        if (message is null)
        {
            return OnNullMessage();
        }

        var validator = services.GetService<IValidator<TMessage>>()
            ?? throw new InvalidOperationException(
                $"No FluentValidation validator is registered for '{typeof(TMessage).Name}'. " +
                $"Call services.AddValidatorsFromAssemblyContaining<YourValidator>() " +
                $"or services.AddScoped<IValidator<{typeof(TMessage).Name}>, YourValidator>().");

        var result = await validator.ValidateAsync(message, cancellationToken).ConfigureAwait(false);

        return result.IsValid
            ? MessageValidationResult.Success
            : MessageValidationResult.Fail(result.Errors.Select(e => e.ErrorMessage));
    }

    /// <summary>
    /// Returns the validation result used when the message is <see langword="null"/>.
    /// The default implementation reports a deterministic validation failure.
    /// </summary>
    protected virtual MessageValidationResult OnNullMessage()
        => MessageValidationResult.Fail(
            "Cannot validate a null message. Pass allowNullMessages: true to "
            + "ValidateWithFluentValidation if null payloads are valid for this "
            + "consumer group, or add a .SkipNullPayloads() filter upstream to "
            + "drop them.");
}

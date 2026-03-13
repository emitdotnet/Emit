namespace Emit.FluentValidation;

using Emit.Abstractions;
using global::FluentValidation;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Bridges FluentValidation's <see cref="IValidator{T}"/> into Emit's
/// <see cref="IMessageValidator{TValue}"/> pipeline. Resolved from DI per message.
/// </summary>
/// <typeparam name="TMessage">The message type to validate.</typeparam>
internal sealed class FluentValidationMessageValidator<TMessage>(
    IServiceProvider services) : IMessageValidator<TMessage>
{
    /// <inheritdoc />
    public async Task<MessageValidationResult> ValidateAsync(
        TMessage message, CancellationToken cancellationToken)
    {
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
}

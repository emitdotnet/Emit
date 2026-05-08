namespace Emit.FluentValidation;

using Emit.Abstractions;

/// <summary>
/// Variant of <see cref="FluentValidationMessageValidator{TMessage}"/> that treats <see langword="null"/>
/// messages as valid and skips FluentValidation invocation. Used by
/// <see cref="FluentValidationExtensions.ValidateWithFluentValidation{TMessage}"/> when
/// <c>allowNullMessages: true</c>. Non-null messages still flow through FluentValidation unchanged.
/// </summary>
/// <typeparam name="TMessage">The message type to validate.</typeparam>
internal sealed class FluentValidationNullTolerantMessageValidator<TMessage>(
    IServiceProvider services) : FluentValidationMessageValidator<TMessage>(services)
{
    /// <inheritdoc />
    protected override MessageValidationResult OnNullMessage()
        => MessageValidationResult.Success;
}

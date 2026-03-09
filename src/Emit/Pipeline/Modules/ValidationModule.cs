namespace Emit.Pipeline.Modules;

using Emit.Abstractions;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Holds validation configuration. Validation failures throw
/// <c>MessageValidationException</c> — there is no per-validation error action.
/// The error policy handles validation failures uniformly with all other errors.
/// </summary>
/// <typeparam name="TValue">The message type to validate.</typeparam>
public sealed class ValidationModule<TValue>
{
    private Type? validatorType;
    private IMessageValidator<TValue>? delegateValidator;

    /// <summary>
    /// Gets whether validation has been configured.
    /// </summary>
    public bool IsConfigured => validatorType is not null || delegateValidator is not null;

    /// <summary>
    /// Registers a class-based validator resolved from DI.
    /// </summary>
    /// <typeparam name="TValidator">The validator type.</typeparam>
    /// <exception cref="InvalidOperationException">Validation has already been configured.</exception>
    public void Configure<TValidator>()
        where TValidator : class, IMessageValidator<TValue>
    {
        EnsureNotConfigured();
        validatorType = typeof(TValidator);
    }

    /// <summary>
    /// Registers an async delegate validator.
    /// </summary>
    /// <param name="validator">The async validator delegate.</param>
    /// <exception cref="InvalidOperationException">Validation has already been configured.</exception>
    public void Configure(Func<TValue, CancellationToken, Task<MessageValidationResult>> validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        EnsureNotConfigured();
        delegateValidator = new DelegateMessageValidator<TValue>(validator);
    }

    /// <summary>
    /// Registers a synchronous delegate validator (wrapped to async internally).
    /// </summary>
    /// <param name="validator">The synchronous validator delegate.</param>
    /// <exception cref="InvalidOperationException">Validation has already been configured.</exception>
    public void Configure(Func<TValue, MessageValidationResult> validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        EnsureNotConfigured();
        delegateValidator = new DelegateMessageValidator<TValue>(
            (msg, _) => Task.FromResult(validator(msg)));
    }

    /// <summary>
    /// Resolves the configured validator. For class-based validators, resolves from DI.
    /// For delegate validators, returns the adapter instance.
    /// </summary>
    internal IMessageValidator<TValue> ResolveValidator(IServiceProvider services)
    {
        if (delegateValidator is not null)
        {
            return delegateValidator;
        }

        return (IMessageValidator<TValue>)services.GetRequiredService(validatorType!);
    }

    private void EnsureNotConfigured()
    {
        if (IsConfigured)
        {
            throw new InvalidOperationException("Validation has already been configured.");
        }
    }
}

/// <summary>
/// Adapter that wraps a validation delegate as an <see cref="IMessageValidator{TValue}"/> implementation.
/// </summary>
internal sealed class DelegateMessageValidator<TValue>(
    Func<TValue, CancellationToken, Task<MessageValidationResult>> validator) : IMessageValidator<TValue>
{
    /// <inheritdoc />
    public Task<MessageValidationResult> ValidateAsync(TValue message, CancellationToken cancellationToken)
    {
        return validator(message, cancellationToken);
    }
}

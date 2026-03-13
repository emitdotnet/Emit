namespace Emit.Pipeline.Modules;

using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Holds validation configuration for a consumer group. Validation failures throw
/// <see cref="MessageValidationException"/>. When a <see cref="ValidationErrorAction"/>
/// is configured, that action applies to validation failures specifically; otherwise they
/// are handled by the group-level error policy.
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
    /// Gets the error action applied to validation failures, or <c>null</c> if not configured.
    /// </summary>
    public ErrorAction? ValidationErrorAction { get; private set; }

    /// <summary>
    /// Registers a class-based validator resolved from DI.
    /// </summary>
    /// <typeparam name="TValidator">The validator type.</typeparam>
    /// <param name="configureAction">Configures the terminal action for validation failures.</param>
    /// <exception cref="InvalidOperationException">Validation has already been configured.</exception>
    public void Configure<TValidator>(Action<ErrorActionBuilder> configureAction)
        where TValidator : class, IMessageValidator<TValue>
    {
        ArgumentNullException.ThrowIfNull(configureAction);
        EnsureNotConfigured();
        validatorType = typeof(TValidator);
        ValidationErrorAction = BuildErrorAction(configureAction);
    }

    /// <summary>
    /// Registers an async delegate validator.
    /// </summary>
    /// <param name="validator">The async validator delegate.</param>
    /// <param name="configureAction">Configures the terminal action for validation failures.</param>
    /// <exception cref="InvalidOperationException">Validation has already been configured.</exception>
    public void Configure(
        Func<TValue, CancellationToken, Task<MessageValidationResult>> validator,
        Action<ErrorActionBuilder> configureAction)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(configureAction);
        EnsureNotConfigured();
        delegateValidator = new DelegateMessageValidator<TValue>(validator);
        ValidationErrorAction = BuildErrorAction(configureAction);
    }

    /// <summary>
    /// Registers a synchronous delegate validator (wrapped to async internally).
    /// </summary>
    /// <param name="validator">The synchronous validator delegate.</param>
    /// <param name="configureAction">Configures the terminal action for validation failures.</param>
    /// <exception cref="InvalidOperationException">Validation has already been configured.</exception>
    public void Configure(
        Func<TValue, MessageValidationResult> validator,
        Action<ErrorActionBuilder> configureAction)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(configureAction);
        EnsureNotConfigured();
        delegateValidator = new DelegateMessageValidator<TValue>(
            (msg, _) => Task.FromResult(validator(msg)));
        ValidationErrorAction = BuildErrorAction(configureAction);
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

    /// <summary>
    /// Registers the class-based validator type in the service collection if one was configured.
    /// </summary>
    public void RegisterServices(IServiceCollection services)
    {
        if (validatorType is not null)
        {
            services.TryAddTransient(validatorType);
        }
    }

    private void EnsureNotConfigured()
    {
        if (IsConfigured)
        {
            throw new InvalidOperationException("Validation has already been configured.");
        }
    }

    private static ErrorAction BuildErrorAction(Action<ErrorActionBuilder> configureAction)
    {
        var builder = new ErrorActionBuilder();
        configureAction(builder);
        return builder.Build();
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

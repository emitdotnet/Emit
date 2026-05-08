namespace Emit.Pipeline.Modules;

using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.Abstractions.Pipeline;
using Emit.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Holds validation configuration for a consumer group and acts as the inbound middleware that
/// validates each message. Validation failures are routed to the terminal action carried by
/// <see cref="ValidationErrorAction"/> (dead-letter or discard) inline; the pipeline short-circuits
/// without invoking the handler. Transient validator exceptions propagate to the group's error
/// policy and are eligible for retry. Implements <see cref="IMiddleware{TContext}"/> directly so
/// it can be inserted into the pipeline without an intermediate adapter.
/// </summary>
/// <typeparam name="TValue">The message type to validate.</typeparam>
public sealed class ValidationMiddleware<TValue> : IMiddleware<ConsumeContext<TValue>>
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
    /// Registers the class-based validator type in the service collection if one was configured.
    /// </summary>
    public void RegisterServices(IServiceCollection services)
    {
        if (validatorType is not null)
        {
            services.TryAddTransient(validatorType);
        }
    }

    /// <inheritdoc />
    public async Task InvokeAsync(
        ConsumeContext<TValue> context,
        IMiddlewarePipeline<ConsumeContext<TValue>> next)
    {
        var validator = delegateValidator ??
            (IMessageValidator<TValue>)context.Services.GetRequiredService(validatorType!);

        var result = await validator.ValidateAsync(context.Message, context.CancellationToken)
            .ConfigureAwait(false);

        var metrics = context.Services.GetRequiredService<EmitMetrics>();

        if (result.IsValid)
        {
            metrics.RecordValidationCompleted("passed", "none");
            await next.InvokeAsync(context).ConfigureAwait(false);
            return;
        }

        var errorMessage = string.Join("; ", result.Errors);

        var logger = context.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger<ValidationMiddleware<TValue>>();
        logger.LogWarning(
            "Validation failed for message {MessageId}: {Errors}",
            context.MessageId, errorMessage);

        var sink = context.Services.GetService<IDeadLetterSink>();
        if (ValidationErrorAction is ErrorAction.DeadLetterAction && sink is not null)
        {
            metrics.RecordValidationCompleted("failed", "dead_letter");

            var headers = DeadLetterHeaders.CreateBase(
                context.TransportContext.Headers,
                typeof(MessageValidationException),
                errorMessage,
                context.TransportContext.GetSourceProperties());

            await sink.ProduceAsync(
                context.TransportContext.RawKey,
                context.TransportContext.RawValue,
                headers,
                context.CancellationToken).ConfigureAwait(false);
        }
        else
        {
            // DiscardAction, or DeadLetterAction without a sink configured.
            metrics.RecordValidationCompleted("failed", "discard");
        }

        // Short-circuit: do not invoke the rest of the pipeline.
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

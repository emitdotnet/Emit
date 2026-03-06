namespace Emit.Consumer;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.Abstractions.Pipeline;
using Emit.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Inbound middleware that validates messages before they reach the handler.
/// When validation fails, the configured terminal action (dead-letter or discard) is executed
/// and the handler is not invoked. If the validator throws, the exception propagates to the
/// error handling middleware above.
/// </summary>
/// <typeparam name="TValue">The message value type.</typeparam>
public sealed class ValidationMiddleware<TValue>(
    ConsumerValidation validation,
    IDeadLetterSink? deadLetterSink,
    Func<string, string?>? resolveDeadLetterDestination,
    EmitMetrics emitMetrics,
    ILogger logger) : IMiddleware<InboundContext<TValue>>
{
    /// <inheritdoc />
    public async Task InvokeAsync(InboundContext<TValue> context, MessageDelegate<InboundContext<TValue>> next)
    {
        var validationStart = Stopwatch.GetTimestamp();

        var result = validation.ValidatorType is not null
            ? await InvokeClassBasedAsync(context).ConfigureAwait(false)
            : await InvokeDelegateAsync(context).ConfigureAwait(false);

        var validationElapsed = Stopwatch.GetElapsedTime(validationStart).TotalSeconds;
        emitMetrics.RecordValidationDuration(validationElapsed);

        if (result.IsValid)
        {
            emitMetrics.RecordValidationCompleted("passed", "none");
            await next(context).ConfigureAwait(false);
            return;
        }

        // Validation failed — determine action and record metric
        var actionName = validation.Action switch
        {
            ErrorAction.DeadLetterAction => "dead_letter",
            ErrorAction.DiscardAction => "discard",
            _ => "discard",
        };
        emitMetrics.RecordValidationCompleted("failed", actionName);

        await HandleValidationFailureAsync(result, context).ConfigureAwait(false);
    }

    private async Task<MessageValidationResult> InvokeClassBasedAsync(InboundContext<TValue> context)
    {
        var validator = (IMessageValidator<TValue>)context.Services.GetRequiredService(validation.ValidatorType!);
        return await validator.ValidateAsync(context.Message, context.CancellationToken).ConfigureAwait(false);
    }

    private Task<MessageValidationResult> InvokeDelegateAsync(InboundContext<TValue> context)
    {
        var func = (Func<TValue, CancellationToken, Task<MessageValidationResult>>)validation.ValidatorDelegate!;
        return func(context.Message, context.CancellationToken);
    }

    private async Task HandleValidationFailureAsync(MessageValidationResult result, InboundContext<TValue> context)
    {
        var source = context.Features.Get<IMessageSourceFeature>();
        var errors = string.Join("; ", result.Errors);

        switch (validation.Action)
        {
            case ErrorAction.DeadLetterAction deadLetter:
                await ExecuteDeadLetterAsync(deadLetter, errors, source, context).ConfigureAwait(false);
                break;

            case ErrorAction.DiscardAction:
                logger.LogWarning(
                    "Validation failed for message from {Source}: {Errors}. Discarding.",
                    source.FormatSource(), errors);
                break;
        }
    }

    private async Task ExecuteDeadLetterAsync(
        ErrorAction.DeadLetterAction deadLetter,
        string errors,
        IMessageSourceFeature? source,
        InboundContext<TValue> context)
    {
        if (deadLetterSink is null)
        {
            logger.LogError(
                "Dead letter sink is not configured; cannot dead-letter validation failure from {Source}. Discarding.",
                source.FormatSource());
            return;
        }

        // Resolve DLQ destination: explicit override > convention > error
        var dlqDestination = deadLetter.TopicName;
        if (string.IsNullOrWhiteSpace(dlqDestination) && resolveDeadLetterDestination is not null && source is not null)
        {
            var sourceTopic = source.Properties.GetValueOrDefault("topic");
            if (sourceTopic is not null)
            {
                dlqDestination = resolveDeadLetterDestination(sourceTopic);
            }
        }

        if (string.IsNullOrWhiteSpace(dlqDestination))
        {
            logger.LogError(
                "Cannot resolve dead letter topic for validation failure from {Source}. Discarding.",
                source.FormatSource());
            return;
        }

        var rawBytes = context.Features.Get<IRawBytesFeature>();
        var originalHeaders = context.Features.Get<IHeadersFeature>();

        var headers = new List<KeyValuePair<string, string>>();

        // Preserve original message headers
        if (originalHeaders is not null)
        {
            headers.AddRange(originalHeaders.Headers);
        }

        // Add diagnostic headers
        headers.Add(new("x-emit-exception-type", "Emit.MessageValidationException"));
        headers.Add(new("x-emit-exception-message", errors));
        headers.Add(new("x-emit-retry-count", "0"));
        if (source is not null)
        {
            foreach (var prop in source.Properties)
            {
                headers.Add(new($"x-emit-source-{prop.Key}", prop.Value));
            }
        }

        headers.Add(new("x-emit-timestamp", DateTimeOffset.UtcNow.ToString("o")));

        try
        {
            await deadLetterSink.ProduceAsync(
                rawBytes?.RawKey,
                rawBytes?.RawValue,
                headers,
                dlqDestination,
                context.CancellationToken).ConfigureAwait(false);

            emitMetrics.RecordDlqProduced("validation_failure", dlqDestination);
        }
        catch
        {
            emitMetrics.RecordDlqProduceErrors("validation_failure", dlqDestination);
            throw;
        }

        logger.LogWarning(
            "Validation failed for message from {Source}: {Errors}. Dead-lettered to {DlqDestination}.",
            source.FormatSource(), errors, dlqDestination);
    }
}

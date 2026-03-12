namespace Emit.Consumer;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Metrics;
using Emit.Pipeline.Modules;
using Microsoft.Extensions.Logging;

/// <summary>
/// Consume-pipeline middleware that validates messages before they reach the handler.
/// On failure, throws <see cref="MessageValidationException"/> — the error policy in
/// <c>ConsumeErrorMiddleware</c> decides whether to dead-letter or discard.
/// </summary>
/// <typeparam name="TValue">The message value type.</typeparam>
internal sealed class ValidationMiddleware<TValue>(
    ValidationModule<TValue> validation,
    EmitMetrics emitMetrics,
    ILogger<ValidationMiddleware<TValue>> logger) : IMiddleware<ConsumeContext<TValue>>
{
    /// <inheritdoc />
    public async Task InvokeAsync(ConsumeContext<TValue> context, IMiddlewarePipeline<ConsumeContext<TValue>> next)
    {
        var validationStart = Stopwatch.GetTimestamp();

        var validator = validation.ResolveValidator(context.Services);
        var result = await validator.ValidateAsync(context.Message, context.CancellationToken).ConfigureAwait(false);

        var validationElapsed = Stopwatch.GetElapsedTime(validationStart).TotalSeconds;
        emitMetrics.RecordValidationDuration(validationElapsed);

        if (result.IsValid)
        {
            emitMetrics.RecordValidationCompleted("passed", "none");
            await next.InvokeAsync(context).ConfigureAwait(false);
            return;
        }

        emitMetrics.RecordValidationCompleted("failed", "exception");

        var errors = result.Errors;
        logger.LogWarning(
            "Validation failed for message {MessageId}: {Errors}",
            context.MessageId, string.Join("; ", errors));

        throw new MessageValidationException(errors);
    }
}

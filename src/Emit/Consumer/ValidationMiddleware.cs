namespace Emit.Consumer;

using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.Abstractions.Pipeline;
using Emit.Metrics;
using Emit.Pipeline.Modules;
using Microsoft.Extensions.Logging;

/// <summary>
/// Consume-pipeline middleware that validates messages before they reach the handler.
/// On <see cref="MessageValidationResult.IsValid">success</see>, the message continues through
/// the pipeline. On failure, the message is dead-lettered or discarded inline per the configured
/// <see cref="ErrorAction"/> and the pipeline short-circuits (the handler is not invoked).
/// Exceptions thrown by the validator propagate to the outer error policy (the standard
/// transient-error path).
/// <para>
/// In batch consumers, this middleware is invoked per-item by
/// <see cref="Pipeline.BatchPerItemAdapter{TItem}"/>. Per-item failures dead-letter that one item;
/// surviving items continue as a reduced batch.
/// </para>
/// </summary>
/// <typeparam name="TValue">The message value type.</typeparam>
internal sealed class ValidationMiddleware<TValue>(
    ValidationModule<TValue> validation,
    ErrorAction validationErrorAction,
    IDeadLetterSink? deadLetterSink,
    EmitMetrics emitMetrics,
    ILogger<ValidationMiddleware<TValue>> logger) : IMiddleware<ConsumeContext<TValue>>
{
    /// <inheritdoc />
    public async Task InvokeAsync(ConsumeContext<TValue> context, IMiddlewarePipeline<ConsumeContext<TValue>> next)
    {
        var validator = validation.ResolveValidator(context.Services);
        var result = await validator.ValidateAsync(context.Message, context.CancellationToken).ConfigureAwait(false);

        if (result.IsValid)
        {
            emitMetrics.RecordValidationCompleted("passed", "none");
            await next.InvokeAsync(context).ConfigureAwait(false);
            return;
        }

        var errors = result.Errors;
        var errorMessage = string.Join("; ", errors);

        logger.LogWarning(
            "Validation failed for message {MessageId}: {Errors}",
            context.MessageId, errorMessage);

        if (validationErrorAction is ErrorAction.DeadLetterAction && deadLetterSink is not null)
        {
            emitMetrics.RecordValidationCompleted("failed", "dead_letter");

            var headers = DeadLetterHeaders.CreateBase(
                context.TransportContext.Headers,
                typeof(MessageValidationException),
                errorMessage,
                context.TransportContext.GetSourceProperties());

            await deadLetterSink.ProduceAsync(
                context.TransportContext.RawKey,
                context.TransportContext.RawValue,
                headers,
                context.CancellationToken).ConfigureAwait(false);
        }
        else
        {
            // DiscardAction, or DeadLetterAction without a sink configured.
            emitMetrics.RecordValidationCompleted("failed", "discard");
        }

        // Short-circuit: do not invoke the rest of the pipeline.
    }
}

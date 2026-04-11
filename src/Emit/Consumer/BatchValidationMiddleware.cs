namespace Emit.Consumer;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.Abstractions.Pipeline;
using Emit.Metrics;
using Emit.Pipeline.Modules;
using Microsoft.Extensions.Logging;

/// <summary>
/// Batch-specific validation middleware that validates each item in the batch individually
/// using the item-level <see cref="IMessageValidator{TValue}"/>. Invalid items are
/// dead-lettered or discarded per the configured error action. Valid items continue
/// as a reduced batch. The batch is replaced in-place via
/// <see cref="MessageContext{T}.WithMessage"/> to preserve pipeline state.
/// </summary>
/// <typeparam name="TValue">The item message type (not the batch type).</typeparam>
internal sealed class BatchValidationMiddleware<TValue>(
    ValidationModule<TValue> itemValidation,
    ErrorAction validationErrorAction,
    IDeadLetterSink? deadLetterSink,
    EmitMetrics emitMetrics,
    ILogger logger) : IMiddleware<ConsumeContext<MessageBatch<TValue>>>
{
    /// <inheritdoc />
    public async Task InvokeAsync(
        ConsumeContext<MessageBatch<TValue>> context,
        IMiddlewarePipeline<ConsumeContext<MessageBatch<TValue>>> next)
    {
        var validator = itemValidation.ResolveValidator(context.Services);
        var originalBatch = context.Message;
        var validItems = new List<BatchItem<TValue>>(originalBatch.Count);
        var invalidCount = 0;

        var start = Stopwatch.GetTimestamp();

        foreach (var item in originalBatch)
        {
            var result = await validator.ValidateAsync(item.Message, context.CancellationToken)
                .ConfigureAwait(false);

            if (result.IsValid)
            {
                validItems.Add(item);
                continue;
            }

            invalidCount++;

            logger.LogWarning(
                "Validation failed for batch item: {Errors}",
                string.Join("; ", result.Errors));

            if (validationErrorAction is ErrorAction.DeadLetterAction && deadLetterSink is not null)
            {
                var headers = DeadLetterHeaders.CreateBase(
                    item.TransportContext.Headers,
                    typeof(MessageValidationException),
                    string.Join("; ", result.Errors),
                    item.TransportContext.GetSourceProperties());

                await deadLetterSink.ProduceAsync(
                    item.TransportContext.RawKey,
                    item.TransportContext.RawValue,
                    headers,
                    context.CancellationToken).ConfigureAwait(false);
            }
            // If DiscardAction: item is silently dropped (already logged above)
        }

        var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
        emitMetrics.RecordValidationDuration(elapsed);
        emitMetrics.RecordValidationCompleted("passed", "none", validItems.Count);
        emitMetrics.RecordValidationCompleted("failed", "filtered", invalidCount);

        // All items invalid → short-circuit, do not invoke the handler
        if (validItems.Count == 0)
            return;

        // Some items filtered → replace batch in-place (preserves Features, Services, etc.)
        if (invalidCount > 0)
        {
            context.WithMessage(new MessageBatch<TValue>(validItems));
        }

        await next.InvokeAsync(context).ConfigureAwait(false);
    }
}

namespace Emit.Consumer;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.Abstractions.Pipeline;
using Emit.Metrics;
using Emit.Tracing;
using Microsoft.Extensions.Logging;

/// <summary>
/// Middleware right inside the process span of the consume pipeline. Catches handler/validation/retry-exhausted
/// errors, records the failure on the process span, and applies the error policy (dead-letter or discard).
/// Consumer identity is baked in at build time by <c>ConsumerPipelineComposer</c> for DLQ headers.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
internal sealed class ConsumeErrorMiddleware<TMessage>(
    Func<Exception, ErrorAction>? evaluatePolicy,
    IDeadLetterSink? deadLetterSink,
    EmitMetrics emitMetrics,
    ILogger<ConsumeErrorMiddleware<TMessage>> logger,
    string identifier,
    Type? consumerType) : IMiddleware<ConsumeContext<TMessage>>
{
    /// <inheritdoc />
    public async Task InvokeAsync(ConsumeContext<TMessage> context, Func<Task> next)
    {
        try
        {
            await next().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(context, ex).ConfigureAwait(false);
        }
    }

    private async Task HandleErrorAsync(ConsumeContext<TMessage> context, Exception exception)
    {
        // The failure is final: the processing of the message failed, whatever is done with it now
        if (Activity.Current is { Kind: ActivityKind.Consumer } process && process.Source == EmitActivitySources.Consumer)
        {
            MessagingActivity.RecordFailure(process, exception);
        }

        if (evaluatePolicy is null)
        {
            // No policy configured — discard with warning
            logger.LogWarning(exception,
                "No error policy configured for consumer {Consumer}. Discarding message {MessageId}",
                identifier, context.MessageId);
            emitMetrics.RecordErrorAction(EmitMetrics.ErrorActionDiscardUnconfigured);
            emitMetrics.RecordDiscard(context.MessageCount);
        }
        else
        {
            var action = evaluatePolicy(exception);

            switch (action)
            {
                case ErrorAction.DeadLetterAction:
                    await ExecuteDeadLetterAsync(exception, context).ConfigureAwait(false);
                    break;

                case ErrorAction.DiscardAction:
                    logger.LogWarning(exception,
                        "Discarding message {MessageId} for consumer {Consumer}",
                        context.MessageId, identifier);
                    emitMetrics.RecordErrorAction(EmitMetrics.ErrorActionDiscard);
                    emitMetrics.RecordDiscard(context.MessageCount);
                    break;
            }
        }
    }

    private async Task ExecuteDeadLetterAsync(
        Exception exception,
        ConsumeContext<TMessage> context)
    {
        if (deadLetterSink is null)
        {
            logger.LogError(exception,
                "Dead letter sink not configured. Cannot dead-letter message {MessageId} for consumer {Consumer}",
                context.MessageId, identifier);
            emitMetrics.RecordErrorAction(EmitMetrics.ErrorActionDeadLetterNoSink);
            return;
        }

        if (context.Message is IBatchMessage batchMessage)
        {
            await ExecuteBatchDeadLetterAsync(exception, batchMessage, context).ConfigureAwait(false);
            return;
        }

        var destinationName = EmitEndpointAddress.GetEntityName(deadLetterSink.DestinationAddress);

        if (await DeadLetterItemAsync(exception, context.TransportContext, context.RetryAttempt, context.CancellationToken).ConfigureAwait(false))
        {
            emitMetrics.RecordErrorAction(EmitMetrics.ErrorActionDeadLetter);
            logger.LogWarning(exception,
                "Dead-lettered message {MessageId} to {Destination} for consumer {Consumer}",
                context.MessageId, destinationName, identifier);
        }
        else
        {
            emitMetrics.RecordErrorAction(EmitMetrics.ErrorActionDeadLetterFailed);
            logger.LogError(exception,
                "Failed to dead-letter message {MessageId} to {Destination} for consumer {Consumer}",
                context.MessageId, destinationName, identifier);
        }
    }

    private async Task ExecuteBatchDeadLetterAsync(
        Exception exception,
        IBatchMessage batchMessage,
        ConsumeContext<TMessage> context)
    {
        var destinationName = EmitEndpointAddress.GetEntityName(deadLetterSink!.DestinationAddress);
        int succeeded = 0;
        int failed = 0;

        foreach (var itemTransport in batchMessage.GetItemTransportContexts())
        {
            if (await DeadLetterItemAsync(exception, itemTransport, context.RetryAttempt, context.CancellationToken).ConfigureAwait(false))
            {
                succeeded++;
            }
            else
            {
                failed++;
                logger.LogError(
                    "Failed to dead-letter batch item {Index}/{Total} to {Destination} for consumer {Consumer}",
                    succeeded + failed, batchMessage.Count, destinationName, identifier);
            }
        }

        if (succeeded > 0)
        {
            emitMetrics.RecordErrorAction(EmitMetrics.ErrorActionDeadLetter);
        }

        if (failed > 0)
        {
            emitMetrics.RecordErrorAction(EmitMetrics.ErrorActionDeadLetterFailed);
        }

        logger.LogWarning(exception,
            "Dead-lettered {Succeeded}/{Total} batch items to {Destination} for consumer {Consumer} ({Failed} failed)",
            succeeded, batchMessage.Count, destinationName, identifier, failed);
    }

    /// <summary>
    /// Dead-letters a single item: builds headers and produces to the sink.
    /// Returns <c>true</c> on success, <c>false</c> if the produce failed.
    /// </summary>
    private async Task<bool> DeadLetterItemAsync(
        Exception exception,
        TransportContext transportContext,
        int retryAttempt,
        CancellationToken cancellationToken)
    {
        var headers = BuildDeadLetterHeaders(exception, transportContext, retryAttempt);

        try
        {
            await deadLetterSink!.ProduceAsync(
                transportContext.RawKey,
                transportContext.RawValue,
                headers,
                cancellationToken).ConfigureAwait(false);
            emitMetrics.RecordDlqProduced(EmitMetrics.DlqReasonErrorPolicy);
            return true;
        }
        catch (Exception)
        {
            emitMetrics.RecordDlqProduceErrors(EmitMetrics.DlqReasonErrorPolicy);
            return false;
        }
    }

    private List<KeyValuePair<string, string>> BuildDeadLetterHeaders(
        Exception exception,
        TransportContext transportContext,
        int retryAttempt)
    {
        var headers = DeadLetterHeaders.CreateBase(
            transportContext.Headers,
            exception.GetType(),
            exception.Message);

        headers.Upsert(DeadLetterHeaders.RetryCount, retryAttempt.ToString());
        headers.Upsert(DeadLetterHeaders.Consumer, identifier);

        if (consumerType is not null)
        {
            headers.Upsert(DeadLetterHeaders.ConsumerType, consumerType.FullName ?? consumerType.Name);
        }

        return headers;
    }
}

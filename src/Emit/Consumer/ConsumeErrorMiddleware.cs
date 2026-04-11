namespace Emit.Consumer;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.Abstractions.Pipeline;
using Emit.Metrics;
using Emit.Tracing;
using Microsoft.Extensions.Logging;

/// <summary>
/// Outermost middleware in the consume pipeline. Catches handler/validation/retry-exhausted
/// errors and applies the error policy (dead-letter or discard). Consumer identity is baked
/// in at build time by <c>ConsumerPipelineComposer</c> for DLQ headers and tracing.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
internal sealed class ConsumeErrorMiddleware<TMessage>(
    Func<Exception, ErrorAction>? evaluatePolicy,
    IDeadLetterSink? deadLetterSink,
    EmitMetrics emitMetrics,
    INodeIdentity nodeIdentity,
    ILogger<ConsumeErrorMiddleware<TMessage>> logger,
    string identifier,
    Type? consumerType,
    ICircuitBreakerNotifier? circuitBreakerNotifier) : IMiddleware<ConsumeContext<TMessage>>
{
    /// <inheritdoc />
    public async Task InvokeAsync(ConsumeContext<TMessage> context, IMiddlewarePipeline<ConsumeContext<TMessage>> next)
    {
        try
        {
            await next.InvokeAsync(context).ConfigureAwait(false);

            if (circuitBreakerNotifier is not null)
            {
                await circuitBreakerNotifier.ReportSuccessAsync().ConfigureAwait(false);
            }
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
        if (evaluatePolicy is null)
        {
            // No policy configured — discard with warning
            logger.LogWarning(exception,
                "No error policy configured for consumer {Consumer}. Discarding message {MessageId}",
                identifier, context.MessageId);
            emitMetrics.RecordErrorAction("discard_unconfigured");
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
                    emitMetrics.RecordErrorAction("discard");
                    break;
            }
        }

        if (circuitBreakerNotifier is not null)
        {
            await circuitBreakerNotifier.ReportFailureAsync(exception).ConfigureAwait(false);
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
            emitMetrics.RecordErrorAction("dead_letter_no_sink");
            return;
        }

        if (context.Message is IBatchMessage batchMessage)
        {
            await ExecuteBatchDeadLetterAsync(exception, batchMessage, context).ConfigureAwait(false);
            return;
        }

        var destinationName = EmitEndpointAddress.GetEntityName(deadLetterSink.DestinationAddress);

        if (await DeadLetterItemAsync(exception, context.TransportContext, context.RetryAttempt, destinationName, context.CancellationToken).ConfigureAwait(false))
        {
            emitMetrics.RecordErrorAction("dead_letter");
            logger.LogWarning(exception,
                "Dead-lettered message {MessageId} to {Destination} for consumer {Consumer}",
                context.MessageId, destinationName, identifier);
        }
        else
        {
            emitMetrics.RecordErrorAction("dead_letter_failed");
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
            if (await DeadLetterItemAsync(exception, itemTransport, context.RetryAttempt, destinationName, context.CancellationToken).ConfigureAwait(false))
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
            emitMetrics.RecordErrorAction("dead_letter");
        }

        if (failed > 0)
        {
            emitMetrics.RecordErrorAction("dead_letter_failed");
        }

        logger.LogWarning(exception,
            "Dead-lettered {Succeeded}/{Total} batch items to {Destination} for consumer {Consumer} ({Failed} failed)",
            succeeded, batchMessage.Count, destinationName, identifier, failed);
    }

    /// <summary>
    /// Dead-letters a single item: builds headers, creates a DLQ activity span, and produces to the sink.
    /// Returns <c>true</c> on success, <c>false</c> if the produce failed.
    /// </summary>
    private async Task<bool> DeadLetterItemAsync(
        Exception exception,
        TransportContext transportContext,
        int retryAttempt,
        string? destinationName,
        CancellationToken cancellationToken)
    {
        var headers = BuildDeadLetterHeaders(exception, transportContext, retryAttempt);

        var parentContext = ExtractParentContext(transportContext.Headers);
        using var dlqActivity = StartDlqActivity(parentContext, destinationName, exception);

        try
        {
            await deadLetterSink!.ProduceAsync(
                transportContext.RawKey,
                transportContext.RawValue,
                headers,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception dlqEx)
        {
            dlqActivity?.SetStatus(ActivityStatusCode.Error, dlqEx.Message);
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
            exception.Message,
            transportContext.GetSourceProperties());

        headers.Add(new(DeadLetterHeaders.RetryCount, retryAttempt.ToString()));
        headers.Add(new(DeadLetterHeaders.Consumer, identifier));

        if (consumerType is not null)
        {
            headers.Add(new(DeadLetterHeaders.ConsumerType, consumerType.FullName ?? consumerType.Name));
        }

        return headers;
    }

    private Activity? StartDlqActivity(
        ActivityContext parentContext,
        string? destinationName,
        Exception exception)
    {
        var dlqActivity = EmitActivitySources.Consumer.StartActivity(
            ActivityNames.DlqPublish,
            ActivityKind.Producer,
            parentContext);

        dlqActivity?.SetTag(ActivityTagNames.NodeId, nodeIdentity.NodeId.ToString());
        dlqActivity?.SetTag(ActivityTagNames.MessagingDestinationName, destinationName);
        dlqActivity?.SetTag(ActivityTagNames.MessagingSystem,
            EmitEndpointAddress.GetScheme(deadLetterSink!.DestinationAddress) ?? "emit");
        dlqActivity?.SetTag(ActivityTagNames.DlqReason, exception.GetType().Name);

        return dlqActivity;
    }

    private static ActivityContext ExtractParentContext(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        if (headers is not { Count: > 0 })
            return default;

        var traceParent = headers.FirstOrDefault(h => h.Key == WellKnownHeaders.TraceParent).Value;
        var traceState = headers.FirstOrDefault(h => h.Key == WellKnownHeaders.TraceState).Value;

        if (!string.IsNullOrEmpty(traceParent) && ActivityContext.TryParse(traceParent, traceState, out var ctx))
            return ctx;

        return default;
    }
}

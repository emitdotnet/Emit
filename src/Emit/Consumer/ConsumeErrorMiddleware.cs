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

        // Build DLQ headers with consumer identity
        var headers = DeadLetterHeaders.CreateBase(
            context.TransportContext.Headers,
            exception.GetType(),
            exception.Message,
            context.TransportContext.GetSourceProperties());

        // Add retry count
        headers.Add(new(DeadLetterHeaders.RetryCount, context.RetryAttempt.ToString()));

        // Add baked consumer identity
        headers.Add(new(DeadLetterHeaders.Consumer, identifier));
        if (consumerType is not null)
        {
            headers.Add(new(DeadLetterHeaders.ConsumerType, consumerType.FullName ?? consumerType.Name));
        }

        // Resume the original trace from message headers so the DLQ span is part of the consume trace
        ActivityContext parentContext = default;
        var transportHeaders = context.TransportContext.Headers;
        if (transportHeaders is { Count: > 0 })
        {
            var traceParent = transportHeaders.FirstOrDefault(h => h.Key == WellKnownHeaders.TraceParent).Value;
            var traceState = transportHeaders.FirstOrDefault(h => h.Key == WellKnownHeaders.TraceState).Value;

            if (!string.IsNullOrEmpty(traceParent))
            {
                ActivityContext.TryParse(traceParent, traceState, out parentContext);
            }
        }

        // Create DLQ publish activity — child of the original trace when headers are present
        var destinationName = EmitEndpointAddress.GetEntityName(deadLetterSink.DestinationAddress);
        using var dlqActivity = EmitActivitySources.Consumer.StartActivity(
            ActivityNames.DlqPublish,
            ActivityKind.Producer,
            parentContext);
        dlqActivity?.SetTag(ActivityTagNames.NodeId, nodeIdentity.NodeId.ToString());
        dlqActivity?.SetTag(ActivityTagNames.MessagingDestinationName, destinationName);
        dlqActivity?.SetTag(ActivityTagNames.MessagingSystem,
            EmitEndpointAddress.GetScheme(deadLetterSink.DestinationAddress) ?? "emit");
        dlqActivity?.SetTag(ActivityTagNames.DlqReason, exception.GetType().Name);

        try
        {
            await deadLetterSink.ProduceAsync(
                context.TransportContext.RawKey,
                context.TransportContext.RawValue,
                headers,
                context.CancellationToken).ConfigureAwait(false);

            emitMetrics.RecordErrorAction("dead_letter");
            logger.LogWarning(exception,
                "Dead-lettered message {MessageId} to {Destination} for consumer {Consumer}",
                context.MessageId, destinationName, identifier);
        }
        catch (Exception dlqEx)
        {
            dlqActivity?.SetStatus(ActivityStatusCode.Error, dlqEx.Message);
            emitMetrics.RecordErrorAction("dead_letter_failed");
            logger.LogError(dlqEx,
                "Failed to dead-letter message {MessageId} to {Destination} for consumer {Consumer}",
                context.MessageId, destinationName, identifier);
        }
    }
}

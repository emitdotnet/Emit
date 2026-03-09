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
    Func<string, string?>? resolveDeadLetterDestination,
    EmitMetrics emitMetrics,
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
        // Evaluate error policy
        if (evaluatePolicy is null)
        {
            // No policy configured — discard with warning
            logger.LogWarning(exception,
                "No error policy configured for consumer {Consumer}. Discarding message {MessageId}",
                identifier, context.MessageId);
            emitMetrics.RecordErrorAction("discard_unconfigured");

            if (circuitBreakerNotifier is not null)
            {
                await circuitBreakerNotifier.ReportFailureAsync(exception).ConfigureAwait(false);
            }

            return;
        }

        var action = evaluatePolicy(exception);

        switch (action)
        {
            case ErrorAction.DeadLetterAction deadLetter:
                await ExecuteDeadLetterAsync(deadLetter, exception, context).ConfigureAwait(false);
                break;

            case ErrorAction.DiscardAction:
                logger.LogWarning(exception,
                    "Discarding message {MessageId} for consumer {Consumer}",
                    context.MessageId, identifier);
                emitMetrics.RecordErrorAction("discard");
                break;
        }

        if (circuitBreakerNotifier is not null)
        {
            await circuitBreakerNotifier.ReportFailureAsync(exception).ConfigureAwait(false);
        }
    }

    private async Task ExecuteDeadLetterAsync(
        ErrorAction.DeadLetterAction deadLetter,
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

        // Resolve DLQ destination
        var dlqDestination = deadLetter.TopicName
            ?? resolveDeadLetterDestination?.Invoke(identifier);

        if (dlqDestination is null)
        {
            logger.LogError(exception,
                "Cannot resolve dead letter destination for consumer {Consumer}, message {MessageId}",
                identifier, context.MessageId);
            emitMetrics.RecordErrorAction("dead_letter_no_destination");
            return;
        }

        // Build DLQ headers with consumer identity
        var headers = DeadLetterHeaders.CreateBase(
            context.TransportContext.Headers,
            exception.GetType(),
            exception.Message,
            sourceProperties: null);

        // Add retry count
        headers.Add(new(DeadLetterHeaders.RetryCount, context.RetryAttempt.ToString()));

        // Add baked consumer identity
        headers.Add(new(DeadLetterHeaders.Consumer, identifier));
        if (consumerType is not null)
        {
            headers.Add(new(DeadLetterHeaders.ConsumerType, consumerType.FullName ?? consumerType.Name));
        }

        // Create DLQ publish activity
        using var dlqActivity = EmitActivitySources.Consumer.StartActivity(
            "emit.dlq.publish",
            ActivityKind.Producer);
        dlqActivity?.SetTag(ActivityTagNames.MessagingDestinationName, dlqDestination);
        dlqActivity?.SetTag(ActivityTagNames.MessagingSystem, "emit");
        dlqActivity?.SetTag(ActivityTagNames.DlqReason, exception.GetType().Name);

        try
        {
            await deadLetterSink.ProduceAsync(
                context.TransportContext.RawKey,
                context.TransportContext.RawValue,
                headers,
                dlqDestination,
                context.CancellationToken).ConfigureAwait(false);

            emitMetrics.RecordErrorAction("dead_letter");
            logger.LogWarning(exception,
                "Dead-lettered message {MessageId} to {Destination} for consumer {Consumer}",
                context.MessageId, dlqDestination, identifier);
        }
        catch (Exception dlqEx)
        {
            dlqActivity?.SetStatus(ActivityStatusCode.Error, dlqEx.Message);
            emitMetrics.RecordErrorAction("dead_letter_failed");
            logger.LogError(dlqEx,
                "Failed to dead-letter message {MessageId} to {Destination} for consumer {Consumer}",
                context.MessageId, dlqDestination, identifier);
        }
    }
}

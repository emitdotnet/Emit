namespace Emit.Consumer;

using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.Abstractions.Observability;
using Emit.Abstractions.Pipeline;
using Emit.Metrics;
using Emit.Pipeline;
using Microsoft.Extensions.Logging;

/// <summary>
/// Inbound middleware that catches exceptions from downstream handlers, evaluates them
/// against an error policy, and executes the appropriate action (retry, dead-letter, discard, or skip).
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
public sealed class ErrorHandlingMiddleware<TMessage>(
    Func<Exception, ErrorAction> evaluatePolicy,
    IDeadLetterSink? deadLetterSink,
    Func<string, string?>? resolveDeadLetterDestination,
    IReadOnlyList<IConsumeObserver> observers,
    EmitMetrics emitMetrics,
    ILogger logger,
    string? unconfiguredConsumerName = null,
    ICircuitBreakerNotifier? circuitBreakerNotifier = null) : IMiddleware<InboundContext<TMessage>>
{
    private static readonly ActivitySource ConsumerActivitySource = new("Emit.Consumer", "1.0.0");
    private bool firstUnconfiguredDiscardLogged;

    /// <inheritdoc />
    public async Task InvokeAsync(InboundContext<TMessage> context, MessageDelegate<InboundContext<TMessage>> next)
    {
        try
        {
            await next(context).ConfigureAwait(false);

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
            await NotifyObserversAsync(context, ex, attempt: 0).ConfigureAwait(false);
            var action = evaluatePolicy(ex);
            var recovered = await ExecuteActionAsync(action, ex, context, next).ConfigureAwait(false);

            if (circuitBreakerNotifier is not null)
            {
                if (recovered)
                {
                    await circuitBreakerNotifier.ReportSuccessAsync().ConfigureAwait(false);
                }
                else
                {
                    await circuitBreakerNotifier.ReportFailureAsync(ex).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task<bool> ExecuteActionAsync(
        ErrorAction action,
        Exception exception,
        InboundContext<TMessage> context,
        MessageDelegate<InboundContext<TMessage>> next)
    {
        switch (action)
        {
            case ErrorAction.RetryAction retry:
                emitMetrics.RecordErrorAction("retry");
                return await ExecuteRetryAsync(retry, exception, context, next).ConfigureAwait(false);

            case ErrorAction.DeadLetterAction deadLetter:
                emitMetrics.RecordErrorAction("dead_letter");
                await ExecuteDeadLetterAsync(deadLetter, exception, context).ConfigureAwait(false);
                return false;

            case ErrorAction.DiscardAction:
                emitMetrics.RecordErrorAction("discard");
                ExecuteDiscard(exception, context);
                return false;

            default:
                // Unknown action type — rethrow the original exception
                ExceptionDispatchInfo.Capture(exception).Throw();
                return false; // Unreachable
        }
    }

    private async Task<bool> ExecuteRetryAsync(
        ErrorAction.RetryAction retry,
        Exception originalException,
        InboundContext<TMessage> context,
        MessageDelegate<InboundContext<TMessage>> next)
    {
        var source = context.Features.Get<IMessageSourceFeature>();
        Exception lastException = originalException;
        var retryStart = Stopwatch.GetTimestamp();

        // Capture parent Activity context for retry attempts
        var parentContext = Activity.Current?.Context ?? default;

        for (var attempt = 0; attempt < retry.MaxAttempts; attempt++)
        {
            var delay = retry.Backoff.CalculateDelay(attempt);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, context.CancellationToken).ConfigureAwait(false);
            }

            // Create Activity for this retry attempt
            using var retryActivity = ConsumerActivitySource.StartActivity(
                "emit.consume.retry",
                ActivityKind.Consumer,
                parentContext);

            retryActivity?.SetTag("messaging.retry.attempt", attempt + 1);
            retryActivity?.SetTag("messaging.retry.max_attempts", retry.MaxAttempts);

            try
            {
                await next(context).ConfigureAwait(false);
                logger.LogDebug(
                    "Retry {Attempt}/{MaxAttempts} succeeded for message from {Source}",
                    attempt + 1, retry.MaxAttempts, source.FormatSource());

                var retryElapsed = Stopwatch.GetElapsedTime(retryStart).TotalSeconds;
                emitMetrics.RecordRetryAttempts(attempt + 1, "success");
                emitMetrics.RecordRetryDuration(retryElapsed, "success");
                return true;
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                retryActivity?.SetStatus(ActivityStatusCode.Error, "Operation cancelled");
                throw;
            }
            catch (Exception ex)
            {
                retryActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);

                lastException = ex;
                await NotifyObserversAsync(context, ex, attempt + 1).ConfigureAwait(false);
                logger.LogWarning(ex,
                    "Retry {Attempt}/{MaxAttempts} failed for message from {Source}",
                    attempt + 1, retry.MaxAttempts, source.FormatSource());
            }
        }

        var exhaustedElapsed = Stopwatch.GetElapsedTime(retryStart).TotalSeconds;
        emitMetrics.RecordRetryAttempts(retry.MaxAttempts, "exhausted");
        emitMetrics.RecordRetryDuration(exhaustedElapsed, "exhausted");

        // All retries exhausted — execute the exhaustion action
        return await ExecuteActionAsync(retry.ExhaustionAction, lastException, context, next).ConfigureAwait(false);
    }

    private async Task ExecuteDeadLetterAsync(
        ErrorAction.DeadLetterAction deadLetter,
        Exception exception,
        InboundContext<TMessage> context)
    {
        var source = context.Features.Get<IMessageSourceFeature>();

        if (deadLetterSink is null)
        {
            logger.LogError(exception,
                "Dead letter sink is not configured; cannot dead-letter message from {Source}. Rethrowing.",
                source.FormatSource());
            ExceptionDispatchInfo.Capture(exception).Throw();
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
            logger.LogError(exception,
                "Cannot resolve dead letter topic for message from {Source}. Rethrowing.",
                source.FormatSource());
            ExceptionDispatchInfo.Capture(exception).Throw();
            return;
        }

        // Create Activity for DLQ publication
        using var dlqActivity = ConsumerActivitySource.StartActivity(
            "emit.dlq.publish",
            ActivityKind.Producer);

        var providerName = source?.Properties.GetValueOrDefault("provider") ?? "unknown";
        dlqActivity?.SetTag("messaging.destination.name", dlqDestination);
        dlqActivity?.SetTag("messaging.system", providerName);
        dlqActivity?.SetTag("emit.dlq.reason", "MaxRetriesExceeded");

        var rawBytes = context.Features.Get<IRawBytesFeature>();
        var originalHeaders = context.Features.Get<IHeadersFeature>();

        var headers = new List<KeyValuePair<string, string>>();

        // Preserve original message headers
        if (originalHeaders is not null)
        {
            headers.AddRange(originalHeaders.Headers);
        }

        // Preserve original trace context for replay linking
        var originalTraceParent = originalHeaders?.Headers.FirstOrDefault(h => h.Key == "traceparent").Value;
        if (!string.IsNullOrEmpty(originalTraceParent))
        {
            headers.Add(new("emit.dlq.original_traceparent", originalTraceParent));
        }

        var originalTopic = source?.Properties.GetValueOrDefault("topic");
        if (!string.IsNullOrEmpty(originalTopic))
        {
            headers.Add(new("emit.dlq.original_topic", originalTopic));
        }

        // Add diagnostic headers
        headers.Add(new("x-emit-exception-type", exception.GetType().FullName ?? exception.GetType().Name));
        headers.Add(new("x-emit-exception-message", exception.Message));
        if (source is not null)
        {
            foreach (var prop in source.Properties)
            {
                headers.Add(new($"x-emit-source-{prop.Key}", prop.Value));
            }
        }

        var retryFeature = context.Features.Get<IRetryAttemptFeature>();
        if (retryFeature is not null)
        {
            headers.Add(new("x-emit-retry-count", retryFeature.Attempt.ToString()));
        }

        // Consumer identity (which handler/router was processing)
        var identity = context.Features.Get<IConsumerIdentityFeature>();
        if (identity is not null)
        {
            headers.Add(new("x-emit-consumer", identity.Identifier));

            if (identity.ConsumerType is not null)
            {
                headers.Add(new("x-emit-consumer-type", identity.ConsumerType.FullName ?? identity.ConsumerType.Name));
            }

            if (identity.RouteKey is not null)
            {
                headers.Add(new("x-emit-route-key", identity.RouteKey.ToString()!));
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

            emitMetrics.RecordDlqProduced("handler_error", dlqDestination);
        }
        catch (Exception ex)
        {
            dlqActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            emitMetrics.RecordDlqProduceErrors("handler_error", dlqDestination);
            throw;
        }

        logger.LogWarning(exception,
            "Dead-lettered message from {Source} to {DlqDestination}",
            source.FormatSource(), dlqDestination);
    }

    private void ExecuteDiscard(Exception exception, InboundContext<TMessage> context)
    {
        var source = context.Features.Get<IMessageSourceFeature>();

        emitMetrics.RecordDiscard();

        if (unconfiguredConsumerName is not null && !firstUnconfiguredDiscardLogged)
        {
            firstUnconfiguredDiscardLogged = true;
            logger.LogWarning(exception,
                "Discarding failed message from {Source} — no OnError configured for consumer {Handler}. Configure OnError to enable retry or dead-lettering.",
                source.FormatSource(), unconfiguredConsumerName);
            return;
        }

        logger.LogWarning(exception,
            "Discarding failed message from {Source}: {ExceptionType}: {ExceptionMessage}",
            source.FormatSource(),
            exception.GetType().Name, exception.Message);
    }

    private async Task NotifyObserversAsync(InboundContext<TMessage> context, Exception exception, int attempt)
    {
        context.Features.Set<IRetryAttemptFeature>(new RetryAttemptFeature(attempt));

        foreach (var observer in observers)
        {
            try
            {
                await observer.OnConsumeErrorAsync(context, exception).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "IConsumeObserver.OnConsumeErrorAsync failed for {ObserverType} on attempt {Attempt}",
                    observer.GetType().Name, attempt);
            }
        }
    }
}

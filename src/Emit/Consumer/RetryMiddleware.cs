namespace Emit.Consumer;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.Abstractions.Pipeline;
using Emit.Metrics;
using Emit.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Consume-pipeline middleware that retries message processing on failure.
/// Each failure is evaluated against the error policy: the clause matching the exception decides
/// whether another attempt runs and with which backoff. Creates an Activity per retry attempt.
/// When no further attempt runs, rethrows the last exception for <c>ConsumeErrorMiddleware</c>.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
internal sealed class RetryMiddleware<TMessage>(
    Func<Exception, ErrorAction> evaluatePolicy,
    EmitMetrics emitMetrics,
    INodeIdentity nodeIdentity,
    ILogger<RetryMiddleware<TMessage>> logger) : IMiddleware<ConsumeContext<TMessage>>
{
    private static readonly ActivitySource ConsumerActivitySource = EmitActivitySources.Consumer;

    /// <inheritdoc />
    public async Task InvokeAsync(ConsumeContext<TMessage> context, IMiddlewarePipeline<ConsumeContext<TMessage>> next)
    {
        // Inner middleware asks the same question this middleware answers, so a failure it treats
        // as final is exactly one that will not be retried.
        context.WillRetry = exception => GetRetryAction(exception, context) is not null;

        try
        {
            await next.InvokeAsync(context).ConfigureAwait(false);
        }
        catch (Exception firstException) when (GetRetryAction(firstException, context) is not null)
        {
            await ExecuteRetryAsync(firstException, context, next).ConfigureAwait(false);
        }
    }

    private ErrorAction.RetryAction? GetRetryAction(Exception exception, ConsumeContext<TMessage> context)
    {
        // A shutdown is never retried: the message is redelivered once the consumer restarts.
        if (exception is OperationCanceledException && context.CancellationToken.IsCancellationRequested)
        {
            return null;
        }

        return evaluatePolicy(exception) is ErrorAction.RetryAction retry && context.RetryAttempt < retry.MaxAttempts
            ? retry
            : null;
    }

    private async Task ExecuteRetryAsync(
        Exception originalException,
        ConsumeContext<TMessage> context,
        IMiddlewarePipeline<ConsumeContext<TMessage>> next)
    {
        Exception lastException = originalException;
        var retryStart = Stopwatch.GetTimestamp();
        var originalServices = context.Services;

        // Capture parent Activity context for retry attempts
        var parentContext = Activity.Current?.Context ?? default;

        while (GetRetryAction(lastException, context) is { } retry)
        {
            var delay = retry.Backoff.CalculateDelay(context.RetryAttempt);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, context.CancellationToken).ConfigureAwait(false);
            }

            // Set retry attempt on context for downstream middleware
            context.RetryAttempt++;

            // Create a child scope so each retry gets fresh scoped services
            await using var retryScope = originalServices.CreateAsyncScope();
            context.WithServices(retryScope.ServiceProvider);

            // Create Activity for this retry attempt
            using var retryActivity = ConsumerActivitySource.StartActivity(
                ActivityNames.ConsumeRetry,
                ActivityKind.Consumer,
                parentContext);

            retryActivity?.SetTag(ActivityTagNames.NodeId, nodeIdentity.NodeId.ToString());
            retryActivity?.SetTag(ActivityTagNames.MessagingRetryAttempt, context.RetryAttempt);
            retryActivity?.SetTag(ActivityTagNames.MessagingRetryMaxAttempts, retry.MaxAttempts);

            try
            {
                await next.InvokeAsync(context).ConfigureAwait(false);

                logger.LogDebug(
                    "Retry {Attempt}/{MaxAttempts} succeeded for message {MessageId}",
                    context.RetryAttempt, retry.MaxAttempts, context.MessageId);

                context.WithServices(originalServices);

                var retryElapsed = Stopwatch.GetElapsedTime(retryStart).TotalSeconds;
                emitMetrics.RecordRetryAttempts(context.RetryAttempt, "success");
                emitMetrics.RecordRetryDuration(retryElapsed, "success");
                return;
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                context.WithServices(originalServices);
                retryActivity?.SetStatus(ActivityStatusCode.Error, "Operation cancelled");
                throw;
            }
            catch (Exception ex)
            {
                retryActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);

                lastException = ex;
                logger.LogWarning(ex,
                    "Retry {Attempt}/{MaxAttempts} failed for message {MessageId}",
                    context.RetryAttempt, retry.MaxAttempts, context.MessageId);
            }
        }

        // Restore original services so ConsumeErrorMiddleware doesn't see a disposed scope
        context.WithServices(originalServices);

        var exhaustedElapsed = Stopwatch.GetElapsedTime(retryStart).TotalSeconds;
        emitMetrics.RecordRetryAttempts(context.RetryAttempt, "exhausted");
        emitMetrics.RecordRetryDuration(exhaustedElapsed, "exhausted");

        // No further attempt runs: rethrow for ConsumeErrorMiddleware to handle
        throw lastException;
    }
}

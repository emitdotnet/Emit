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
/// whether another attempt runs and with which backoff. Each retry attempt gets its own span, inside the process
/// span of the message, recording whether that attempt failed.
/// When no further attempt runs, rethrows the last exception for <c>ConsumeErrorMiddleware</c>.
/// An attempt includes filtering and validation, so a filter or validator that throws is retried like a handler.
/// With a circuit breaker, each attempt waits until the circuit lets it run, and its outcome is reported to the
/// circuit breaker as soon as it ends. Without a retry policy, the message gets a single attempt.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
internal sealed class RetryMiddleware<TMessage>(
    Func<Exception, ErrorAction>? evaluatePolicy,
    EmitMetrics emitMetrics,
    EmitTracingOptions tracingOptions,
    INodeIdentity nodeIdentity,
    ILogger<RetryMiddleware<TMessage>> logger,
    ICircuitBreakerNotifier? circuitBreakerNotifier = null) : IMiddleware<ConsumeContext<TMessage>>
{

    /// <inheritdoc />
    public async Task InvokeAsync(ConsumeContext<TMessage> context, IMiddlewarePipeline<ConsumeContext<TMessage>> next)
    {
        // Inner middleware asks the same question this middleware answers, so a failure it treats
        // as final is exactly one that will not be retried.
        context.WillRetry = exception => GetRetryAction(exception, context) is not null;

        var attempt = await BeginAttemptAsync(context).ConfigureAwait(false);
        try
        {
            await RunAttemptAsync(context, next, attempt).ConfigureAwait(false);
        }
        catch (Exception firstException) when (GetRetryAction(firstException, context) is not null)
        {
            await ExecuteRetryAsync(firstException, context, next).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Waits until the circuit breaker, if any, lets an attempt run.
    /// </summary>
    private async ValueTask<IAsyncDisposable?> BeginAttemptAsync(ConsumeContext<TMessage> context) =>
        circuitBreakerNotifier is null
            ? null
            : await circuitBreakerNotifier.BeginAttemptAsync(context.CancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Runs one attempt and, with a circuit breaker, reports its outcome before the attempt ends: a failure is
    /// reported before any backoff or error action.
    /// </summary>
    private async Task RunAttemptAsync(
        ConsumeContext<TMessage> context,
        IMiddlewarePipeline<ConsumeContext<TMessage>> next,
        IAsyncDisposable? attempt)
    {
        if (circuitBreakerNotifier is null || attempt is null)
        {
            await next.InvokeAsync(context).ConfigureAwait(false);
            return;
        }

        await using var _ = attempt.ConfigureAwait(false);
        try
        {
            await next.InvokeAsync(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // A shutdown is not a failure of the attempt
            throw;
        }
        catch (Exception exception)
        {
            await circuitBreakerNotifier.ReportFailureAsync(exception).ConfigureAwait(false);
            throw;
        }

        await circuitBreakerNotifier.ReportSuccessAsync().ConfigureAwait(false);
    }

    private ErrorAction.RetryAction? GetRetryAction(Exception exception, ConsumeContext<TMessage> context)
    {
        // A shutdown is never retried: the message is redelivered once the consumer restarts.
        if (exception is OperationCanceledException && context.CancellationToken.IsCancellationRequested)
        {
            return null;
        }

        return evaluatePolicy?.Invoke(exception) is ErrorAction.RetryAction retry && context.RetryAttempt < retry.MaxAttempts
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

        // The attempts are spans of the process span
        var parentContext = Activity.Current?.Context ?? default;

        while (GetRetryAction(lastException, context) is { } retry)
        {
            var delay = retry.Backoff.CalculateDelay(context.RetryAttempt);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, context.CancellationToken).ConfigureAwait(false);
            }

            // Waiting for the circuit breaker does not spend an attempt: the next one only starts once it may run
            var attempt = await BeginAttemptAsync(context).ConfigureAwait(false);

            // Set retry attempt on context for downstream middleware
            context.RetryAttempt++;

            // Create a child scope so each retry gets fresh scoped services
            await using var retryScope = originalServices.CreateAsyncScope();
            context.WithServices(retryScope.ServiceProvider);

            using var retryActivity = MessagingActivity.StartActivity(
                EmitActivitySources.Consumer,
                tracingOptions,
                ActivityNames.Retry,
                ActivityKind.Internal,
                parentContext,
                tags:
                [
                    new(ActivityTagNames.NodeId, nodeIdentity.NodeId.ToString()),
                    new(ActivityTagNames.RetryAttempt, context.RetryAttempt),
                    new(ActivityTagNames.RetryMaxAttempts, retry.MaxAttempts),
                ]);

            try
            {
                await RunAttemptAsync(context, next, attempt).ConfigureAwait(false);

                logger.LogDebug(
                    "Retry {Attempt}/{MaxAttempts} succeeded for message {MessageId}",
                    context.RetryAttempt, retry.MaxAttempts, context.MessageId);

                context.WithServices(originalServices);

                var retryElapsed = Stopwatch.GetElapsedTime(retryStart).TotalSeconds;
                emitMetrics.RecordRetryAttempts(context.RetryAttempt, EmitMetrics.ResultSuccess);
                emitMetrics.RecordRetryDuration(retryElapsed, EmitMetrics.ResultSuccess);
                return;
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                context.WithServices(originalServices);
                throw;
            }
            catch (Exception ex)
            {
                MessagingActivity.RecordFailure(retryActivity, ex);

                lastException = ex;
                logger.LogWarning(ex,
                    "Retry {Attempt}/{MaxAttempts} failed for message {MessageId}",
                    context.RetryAttempt, retry.MaxAttempts, context.MessageId);
            }
        }

        // Restore original services so ConsumeErrorMiddleware doesn't see a disposed scope
        context.WithServices(originalServices);

        var exhaustedElapsed = Stopwatch.GetElapsedTime(retryStart).TotalSeconds;
        emitMetrics.RecordRetryAttempts(context.RetryAttempt, EmitMetrics.ResultExhausted);
        emitMetrics.RecordRetryDuration(exhaustedElapsed, EmitMetrics.ResultExhausted);

        // No further attempt runs: rethrow for ConsumeErrorMiddleware to handle
        throw lastException;
    }
}

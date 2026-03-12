namespace Emit.Consumer;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Metrics;
using Emit.Pipeline.Modules;
using Emit.Tracing;
using Microsoft.Extensions.Logging;

/// <summary>
/// Consume-pipeline middleware that retries message processing on failure.
/// Creates an <c>emit.consume.retry</c> Activity per retry attempt. On exhaustion,
/// rethrows the last exception for <c>ConsumeErrorMiddleware</c> to handle.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
internal sealed class RetryMiddleware<TMessage>(
    RetryConfig config,
    EmitMetrics emitMetrics,
    ILogger<RetryMiddleware<TMessage>> logger) : IMiddleware<ConsumeContext<TMessage>>
{
    private static readonly ActivitySource ConsumerActivitySource = EmitActivitySources.Consumer;

    /// <inheritdoc />
    public async Task InvokeAsync(ConsumeContext<TMessage> context, IMiddlewarePipeline<ConsumeContext<TMessage>> next)
    {
        try
        {
            await next.InvokeAsync(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception firstException)
        {
            await ExecuteRetryAsync(firstException, context, next).ConfigureAwait(false);
        }
    }

    private async Task ExecuteRetryAsync(
        Exception originalException,
        ConsumeContext<TMessage> context,
        IMiddlewarePipeline<ConsumeContext<TMessage>> next)
    {
        Exception lastException = originalException;
        var retryStart = Stopwatch.GetTimestamp();

        // Capture parent Activity context for retry attempts
        var parentContext = Activity.Current?.Context ?? default;

        for (var attempt = 0; attempt < config.MaxAttempts; attempt++)
        {
            var delay = config.Backoff.CalculateDelay(attempt);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, context.CancellationToken).ConfigureAwait(false);
            }

            // Set retry attempt on context for downstream middleware
            context.RetryAttempt = attempt + 1;

            // Create Activity for this retry attempt
            using var retryActivity = ConsumerActivitySource.StartActivity(
                "emit.consume.retry",
                ActivityKind.Consumer,
                parentContext);

            retryActivity?.SetTag("messaging.retry.attempt", attempt + 1);
            retryActivity?.SetTag("messaging.retry.max_attempts", config.MaxAttempts);

            try
            {
                await next.InvokeAsync(context).ConfigureAwait(false);

                logger.LogDebug(
                    "Retry {Attempt}/{MaxAttempts} succeeded for message {MessageId}",
                    attempt + 1, config.MaxAttempts, context.MessageId);

                var retryElapsed = Stopwatch.GetElapsedTime(retryStart).TotalSeconds;
                emitMetrics.RecordRetryAttempts(attempt + 1, "success");
                emitMetrics.RecordRetryDuration(retryElapsed, "success");
                return;
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
                logger.LogWarning(ex,
                    "Retry {Attempt}/{MaxAttempts} failed for message {MessageId}",
                    attempt + 1, config.MaxAttempts, context.MessageId);
            }
        }

        var exhaustedElapsed = Stopwatch.GetElapsedTime(retryStart).TotalSeconds;
        emitMetrics.RecordRetryAttempts(config.MaxAttempts, "exhausted");
        emitMetrics.RecordRetryDuration(exhaustedElapsed, "exhausted");

        // All retries exhausted — rethrow for ConsumeErrorMiddleware to handle
        throw lastException;
    }
}

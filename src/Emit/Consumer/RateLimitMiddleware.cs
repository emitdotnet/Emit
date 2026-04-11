namespace Emit.Consumer;

using System.Diagnostics;
using System.Threading.RateLimiting;
using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Metrics;

/// <summary>
/// Inbound middleware that throttles message processing using a shared <see cref="RateLimiter"/>.
/// Acquires a single permit before allowing the message to proceed through the pipeline.
/// The rate limiter is shared across all workers in the consumer group.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
public sealed class RateLimitMiddleware<TMessage>(
    RateLimiter rateLimiter,
    EmitMetrics emitMetrics) : IMiddleware<ConsumeContext<TMessage>>
{
    /// <inheritdoc />
    public async Task InvokeAsync(ConsumeContext<TMessage> context, IMiddlewarePipeline<ConsumeContext<TMessage>> next)
    {
        var permitCount = context.Message is IBatchMessage batch ? batch.Count : 1;

        var start = Stopwatch.GetTimestamp();
        using var lease = await rateLimiter.AcquireAsync(permitCount, context.CancellationToken).ConfigureAwait(false);
        var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;

        emitMetrics.RecordRateLimitWaitDuration(elapsed);

        if (!lease.IsAcquired)
        {
            throw new InvalidOperationException(
                $"Rate limiter rejected request for {permitCount} permits. " +
                "Ensure the rate limiter's max permits >= BatchOptions.MaxSize.");
        }

        await next.InvokeAsync(context).ConfigureAwait(false);
    }
}

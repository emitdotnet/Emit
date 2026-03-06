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
    EmitMetrics emitMetrics) : IMiddleware<InboundContext<TMessage>>
{
    /// <inheritdoc />
    public async Task InvokeAsync(InboundContext<TMessage> context, MessageDelegate<InboundContext<TMessage>> next)
    {
        var start = Stopwatch.GetTimestamp();
        using var lease = await rateLimiter.AcquireAsync(1, context.CancellationToken).ConfigureAwait(false);
        var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;

        emitMetrics.RecordRateLimitAcquired();
        emitMetrics.RecordRateLimitWaitDuration(elapsed);

        await next(context).ConfigureAwait(false);
    }
}

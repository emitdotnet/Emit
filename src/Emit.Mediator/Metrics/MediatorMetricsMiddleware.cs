namespace Emit.Mediator.Metrics;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.Pipeline;

/// <summary>
/// Inbound middleware that records mediator dispatch duration, completion, and concurrency metrics.
/// Auto-inserted as the outermost layer of the mediator pipeline.
/// </summary>
/// <typeparam name="TMessage">The request type.</typeparam>
internal sealed class MediatorMetricsMiddleware<TMessage>(
    MediatorMetrics metrics) : IMiddleware<InboundContext<TMessage>>
{
    private static readonly string RequestTypeName = typeof(TMessage).Name;

    /// <inheritdoc />
    public async Task InvokeAsync(InboundContext<TMessage> context, MessageDelegate<InboundContext<TMessage>> next)
    {
        metrics.RecordSendActiveIncrement();
        var start = Stopwatch.GetTimestamp();

        try
        {
            await next(context).ConfigureAwait(false);

            var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            metrics.RecordSendDuration(elapsed, RequestTypeName, "success");
            metrics.RecordSendCompleted(RequestTypeName, "success");
        }
        catch
        {
            var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            metrics.RecordSendDuration(elapsed, RequestTypeName, "error");
            metrics.RecordSendCompleted(RequestTypeName, "error");
            throw;
        }
        finally
        {
            metrics.RecordSendActiveDecrement();
        }
    }
}

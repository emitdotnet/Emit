namespace Emit.Metrics;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.Pipeline;

/// <summary>
/// Outbound middleware that records pipeline duration and completion metrics.
/// Auto-inserted as the outermost layer of the outbound pipeline.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
internal sealed class ProduceMetricsMiddleware<TMessage>(
    EmitMetrics metrics) : IMiddleware<SendContext<TMessage>>
{
    /// <inheritdoc />
    public async Task InvokeAsync(SendContext<TMessage> context, IMiddlewarePipeline<SendContext<TMessage>> next)
    {
        var provider = EmitEndpointAddress.GetScheme(context.DestinationAddress) ?? "unknown";
        var start = Stopwatch.GetTimestamp();

        try
        {
            await next.InvokeAsync(context).ConfigureAwait(false);

            var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            metrics.RecordProduceCompleted(elapsed, provider, "success");
        }
        catch
        {
            var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            metrics.RecordProduceCompleted(elapsed, provider, "error");
            throw;
        }
    }
}

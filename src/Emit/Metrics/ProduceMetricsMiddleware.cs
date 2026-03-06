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
    EmitMetrics metrics) : IMiddleware<OutboundContext<TMessage>>
{
    /// <inheritdoc />
    public async Task InvokeAsync(OutboundContext<TMessage> context, MessageDelegate<OutboundContext<TMessage>> next)
    {
        var provider = context.Features.Get<IProviderIdentifierFeature>()?.ProviderId ?? "unknown";
        var start = Stopwatch.GetTimestamp();

        try
        {
            await next(context).ConfigureAwait(false);

            var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            metrics.RecordProduceDuration(elapsed, provider, "success");
            metrics.RecordProduceCompleted(provider, "success");
        }
        catch
        {
            var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            metrics.RecordProduceDuration(elapsed, provider, "error");
            metrics.RecordProduceCompleted(provider, "error");
            throw;
        }
    }
}

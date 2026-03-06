namespace Emit.Metrics;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.Pipeline;

/// <summary>
/// Inbound middleware that records pipeline duration and completion metrics.
/// Auto-inserted as the outermost layer of the inbound pipeline.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
internal sealed class ConsumeMetricsMiddleware<TMessage>(
    EmitMetrics metrics) : IMiddleware<InboundContext<TMessage>>
{
    /// <inheritdoc />
    public async Task InvokeAsync(InboundContext<TMessage> context, MessageDelegate<InboundContext<TMessage>> next)
    {
        var provider = context.Features.Get<IProviderIdentifierFeature>()?.ProviderId ?? "unknown";
        var start = Stopwatch.GetTimestamp();

        try
        {
            await next(context).ConfigureAwait(false);

            var consumer = context.Features.Get<IConsumerIdentityFeature>()?.Identifier ?? "unknown";
            var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            metrics.RecordConsumeDuration(elapsed, provider, "success", consumer);
            metrics.RecordConsumeCompleted(provider, "success", consumer);
        }
        catch
        {
            var consumer = context.Features.Get<IConsumerIdentityFeature>()?.Identifier ?? "unknown";
            var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            metrics.RecordConsumeDuration(elapsed, provider, "error", consumer);
            metrics.RecordConsumeCompleted(provider, "error", consumer);
            throw;
        }
    }
}

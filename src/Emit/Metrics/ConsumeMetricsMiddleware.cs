namespace Emit.Metrics;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.Pipeline;

/// <summary>
/// Inbound middleware that records pipeline duration and completion metrics.
/// Consumer identity is baked in at build time by <see cref="Pipeline.ConsumerPipelineComposer{TValue}"/>.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
internal sealed class ConsumeMetricsMiddleware<TMessage>(
    EmitMetrics metrics,
    string consumerIdentifier) : IMiddleware<ConsumeContext<TMessage>>
{
    /// <inheritdoc />
    public async Task InvokeAsync(ConsumeContext<TMessage> context, IMiddlewarePipeline<ConsumeContext<TMessage>> next)
    {
        var provider = EmitEndpointAddress.GetScheme(context.DestinationAddress) ?? "unknown";
        var start = Stopwatch.GetTimestamp();

        try
        {
            await next.InvokeAsync(context).ConfigureAwait(false);

            var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            metrics.RecordConsumeCompleted(elapsed, provider, "success", consumerIdentifier);
        }
        catch
        {
            var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            metrics.RecordConsumeCompleted(elapsed, provider, "error", consumerIdentifier);
            throw;
        }
    }
}

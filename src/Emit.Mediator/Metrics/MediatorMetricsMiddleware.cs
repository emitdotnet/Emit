namespace Emit.Mediator.Metrics;

using System.Diagnostics;
using Emit.Abstractions.Pipeline;

/// <summary>
/// Inbound middleware that records mediator dispatch duration, completion, and concurrency metrics.
/// Auto-inserted right inside the tracing layer of the mediator pipeline.
/// </summary>
/// <typeparam name="TMessage">The request type.</typeparam>
internal sealed class MediatorMetricsMiddleware<TMessage>(
    MediatorMetrics metrics) : IMiddleware<MediatorContext<TMessage>>
{
    private static readonly string RequestTypeName = typeof(TMessage).Name;

    /// <inheritdoc />
    public async Task InvokeAsync(MediatorContext<TMessage> context, IMiddlewarePipeline<MediatorContext<TMessage>> next)
    {
        metrics.RecordSendActiveIncrement();
        var start = Stopwatch.GetTimestamp();

        try
        {
            await next.InvokeAsync(context).ConfigureAwait(false);

            var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            metrics.RecordSendCompleted(elapsed, RequestTypeName, MediatorMetrics.ResultSuccess);
        }
        catch
        {
            var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            metrics.RecordSendCompleted(elapsed, RequestTypeName, MediatorMetrics.ResultError);
            throw;
        }
        finally
        {
            metrics.RecordSendActiveDecrement();
        }
    }
}

namespace Emit.Pipeline;

using Emit.Abstractions;
using Emit.Metrics;
using Emit.Observability;
using Emit.Tracing;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Composes the pipeline of a producer. Pipeline order (outermost → innermost):
/// tracing → metrics → observers → user middleware levels → terminal.
/// </summary>
internal static class ProducerPipelineComposer
{
    /// <summary>
    /// Composes the user middleware of <paramref name="levels"/>, outermost first, around
    /// <paramref name="terminal"/>, inside the built-in produce middleware.
    /// </summary>
    /// <typeparam name="TValue">The message value type.</typeparam>
    internal static IMiddlewarePipeline<SendContext<TValue>> Compose<TValue>(
        IServiceProvider services,
        IMiddlewarePipeline<SendContext<TValue>> terminal,
        params MessagePipelineBuilder?[] levels)
    {
        var pipeline = MessagePipelineBuilder.Build<SendContext<TValue>, TValue>(services, terminal, levels);
        pipeline = new MiddlewarePipeline<SendContext<TValue>>(
            services.GetRequiredService<ProduceObserverMiddleware<TValue>>(), pipeline);
        pipeline = new MiddlewarePipeline<SendContext<TValue>>(
            services.GetRequiredService<ProduceMetricsMiddleware<TValue>>(), pipeline);
        return new MiddlewarePipeline<SendContext<TValue>>(
            services.GetRequiredService<ProduceTracingMiddleware<TValue>>(), pipeline);
    }
}

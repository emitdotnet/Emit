namespace Emit.Pipeline;

using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.Abstractions.Observability;
using Emit.Abstractions.Pipeline;
using Emit.Consumer;
using Emit.Metrics;
using Emit.Middleware;
using Emit.Observability;
using Emit.Pipeline.Modules;
using Emit.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Composes a fully-layered consume middleware pipeline for a single consumer or router entry.
/// Pipeline order (innermost → outermost):
/// handler → per-entry → unit of work → validation → filter → retry → group/provider/global → metrics → observers → error → tracing.
/// </summary>
/// <typeparam name="TValue">The message value type.</typeparam>
internal sealed class ConsumerPipelineComposer<TValue>
{
    /// <summary>
    /// Gets the service provider used to resolve middleware instances and dependencies.
    /// </summary>
    public required IServiceProvider Services { get; init; }

    /// <summary>
    /// Gets the logger factory used to create loggers for middleware instances.
    /// </summary>
    public required ILoggerFactory LoggerFactory { get; init; }

    /// <summary>
    /// Gets the ordered list of consume observers notified around the pipeline.
    /// </summary>
    public required IReadOnlyList<IConsumeObserver> ConsumeObservers { get; init; }

    /// <summary>
    /// Gets the consumer group pipeline builder applied around all entries in the group.
    /// </summary>
    public required IMessagePipelineBuilder GroupPipeline { get; init; }

    /// <summary>
    /// Gets the global inbound pipeline builder applied outermost across all providers and groups.
    /// </summary>
    public required IMessagePipelineBuilder GlobalInboundPipeline { get; init; }

    /// <summary>
    /// Gets the provider-level inbound pipeline builder applied between the global and group pipelines.
    /// </summary>
    public required IMessagePipelineBuilder ProviderInboundPipeline { get; init; }

    /// <summary>
    /// Gets the consumer group's <see cref="ValidationMiddleware{TValue}"/> as inbound middleware.
    /// For single consumers it composes directly; for batch consumers the framework wraps it in
    /// <see cref="BatchPerItemAdapter{TItem}"/> before setting this slot. Validation handles
    /// dead-lettering or discarding inline on failure and short-circuits the pipeline. It runs inside
    /// each attempt, so a validator that throws is retried like a handler.
    /// </summary>
    public IMiddleware<ConsumeContext<TValue>>? ValidationMiddleware { get; init; }

    /// <summary>
    /// Gets the consumer group's <see cref="Modules.FilterMiddleware{TValue}"/> as inbound middleware.
    /// For single consumers it composes directly; for batch consumers the framework wraps it in
    /// <see cref="BatchPerItemAdapter{TItem}"/> before setting this slot. Filtered messages
    /// short-circuit before reaching validation. It runs inside each attempt, so a filter that throws
    /// is retried like a handler.
    /// </summary>
    public IMiddleware<ConsumeContext<TValue>>? FilterMiddleware { get; init; }

    /// <summary>
    /// Gets the optional retry policy: the group error policy evaluator, before retry actions are
    /// stripped. When set, a retry middleware wraps the unit of work and retries each failure
    /// whose matching action is a retry, until that action's attempts are used up.
    /// </summary>
    public Func<Exception, ErrorAction>? RetryPolicy { get; init; }

    /// <summary>
    /// Gets the optional error policy evaluator. When <see langword="null"/>, failed
    /// messages are discarded with a warning that identifies the consumer by name.
    /// </summary>
    public Func<Exception, ErrorAction>? ErrorPolicy { get; init; }

    /// <summary>
    /// Gets the optional circuit breaker notifier. When set, the retry middleware holds each attempt
    /// until the circuit lets it run and reports its outcome.
    /// </summary>
    public ICircuitBreakerNotifier? CircuitBreakerNotifier { get; init; }

    /// <summary>
    /// Composes a complete consume pipeline for a single consumer or router entry.
    /// </summary>
    /// <param name="terminal">The innermost delegate that invokes the consumer handler.</param>
    /// <param name="perEntryPipeline">
    /// Optional middleware pipeline specific to this entry. Applied inside the unit of work,
    /// between the handler and the unit-of-work middleware.
    /// </param>
    /// <param name="identifier">
    /// The entry identifier used in error messages, metrics, and tracing. For direct consumers
    /// this is the handler type name; for routers this is the user-provided router identifier.
    /// </param>
    /// <param name="kind">Whether this entry is a direct consumer or a content-based router.</param>
    /// <param name="consumerType">
    /// The handler type for direct consumers, or <see langword="null"/> for routers.
    /// </param>
    /// <returns>
    /// A <see cref="ConsumerPipelineEntry{TValue}"/> pairing the composed pipeline with its identity metadata.
    /// </returns>
    public ConsumerPipelineEntry<TValue> Compose(
        IMiddlewarePipeline<ConsumeContext<TValue>> terminal,
        IMessagePipelineBuilder? perEntryPipeline,
        string identifier,
        ConsumerKind kind,
        Type? consumerType)
    {
        // === Build pipeline innermost → outermost ===

        // 1. terminal = handler/router (already provided)

        // 2. Per-entry middleware
        if (perEntryPipeline is not null)
            terminal = perEntryPipeline.Build<ConsumeContext<TValue>, TValue>(Services, terminal);

        // 2.5. Unit of work (wraps the handler and its own middleware, directly inside retry, so
        // nothing within an attempt can fail after the commit). Routers have none here: each route
        // gets its own inside the router invoker.
        if (consumerType is not null)
        {
            var unitOfWorkMw = new UnitOfWorkMiddleware<ConsumeContext<TValue>>(
                consumerType,
                LoggerFactory.CreateLogger<UnitOfWorkMiddleware<ConsumeContext<TValue>>>());
            terminal = new MiddlewarePipeline<ConsumeContext<TValue>>(unitOfWorkMw, terminal);
        }

        // 3. ValidationMiddleware (if configured). Inside each attempt: a failed validation result short-circuits
        // with its own action and is never retried, while a validator that throws is a failed attempt
        if (ValidationMiddleware is not null)
        {
            terminal = new MiddlewarePipeline<ConsumeContext<TValue>>(ValidationMiddleware, terminal);
        }

        // 3.5. FilterMiddleware (outside validation, so filtered items never reach the validator; inside each
        // attempt, so a filter that throws is a failed attempt)
        if (FilterMiddleware is not null)
        {
            terminal = new MiddlewarePipeline<ConsumeContext<TValue>>(FilterMiddleware, terminal);
        }

        // 4. RetryMiddleware: with a retry policy, or a circuit breaker that holds and counts each attempt, which
        // is a single one without a retry policy
        if (RetryPolicy is not null || CircuitBreakerNotifier is not null)
        {
            var retryMw = new RetryMiddleware<TValue>(
                RetryPolicy,
                Services.GetRequiredService<EmitMetrics>(),
                Services.GetRequiredService<IOptions<EmitTracingOptions>>().Value,
                Services.GetRequiredService<INodeIdentity>(),
                LoggerFactory.CreateLogger<RetryMiddleware<TValue>>(),
                CircuitBreakerNotifier);
            terminal = new MiddlewarePipeline<ConsumeContext<TValue>>(retryMw, terminal);
        }

        // 5. Group → Provider → Global user middleware
        var pipeline = GroupPipeline.Build<ConsumeContext<TValue>, TValue>(
            Services, terminal, GlobalInboundPipeline, ProviderInboundPipeline);

        // 6. ConsumeMetricsMiddleware (baked identity)
        {
            var metricsMw = new ConsumeMetricsMiddleware<TValue>(
                Services.GetRequiredService<EmitMetrics>(),
                identifier);
            pipeline = new MiddlewarePipeline<ConsumeContext<TValue>>(metricsMw, pipeline);
        }

        // 7. ConsumeObserverMiddleware
        {
            var observerMw = new ConsumeObserverMiddleware<TValue>(
                ConsumeObservers,
                LoggerFactory.CreateLogger<ConsumeObserverMiddleware<TValue>>());
            pipeline = new MiddlewarePipeline<ConsumeContext<TValue>>(observerMw, pipeline);
        }

        // 8. ConsumeErrorMiddleware (catches everything inside it, baked identity)
        {
            var errorMw = new ConsumeErrorMiddleware<TValue>(
                ErrorPolicy,
                Services.GetService<IDeadLetterSink>(),
                Services.GetRequiredService<EmitMetrics>(),
                LoggerFactory.CreateLogger<ConsumeErrorMiddleware<TValue>>(),
                identifier,
                consumerType);
            pipeline = new MiddlewarePipeline<ConsumeContext<TValue>>(errorMw, pipeline);
        }

        // 9. ConsumeTracingMiddleware (outermost, baked identity): the process span covers the whole processing of the
        // message, including what the error handling does when it fails
        {
            var tracingMw = new ConsumeTracingMiddleware<TValue>(
                Services.GetRequiredService<IOptions<EmitTracingOptions>>(),
                Services.GetRequiredService<ActivityEnricherInvoker>(),
                Services.GetRequiredService<INodeIdentity>(),
                identifier,
                consumerType);
            pipeline = new MiddlewarePipeline<ConsumeContext<TValue>>(tracingMw, pipeline);
        }

        return new ConsumerPipelineEntry<TValue>
        {
            Identifier = identifier,
            Kind = kind,
            ConsumerType = consumerType,
            Pipeline = pipeline,
        };
    }
}

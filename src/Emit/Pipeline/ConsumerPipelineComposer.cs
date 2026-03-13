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
/// handler → per-entry → retry → validation → group/provider/global → metrics → tracing → observers → error.
/// </summary>
/// <typeparam name="TValue">The message value type.</typeparam>
public sealed class ConsumerPipelineComposer<TValue>
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
    /// Gets the optional validation module. When configured, a validation middleware is inserted
    /// between retry and user middleware. On failure, throws <see cref="MessageValidationException"/>.
    /// </summary>
    public ValidationModule<TValue>? Validation { get; init; }

    /// <summary>
    /// Gets the optional retry configuration. When set, a retry middleware wraps the handler
    /// and per-entry middleware. On exhaustion, rethrows for error handling.
    /// </summary>
    public RetryConfig? RetryConfig { get; init; }

    /// <summary>
    /// Gets the optional error policy evaluator. When <see langword="null"/>, failed
    /// messages are discarded with a warning that identifies the consumer by name.
    /// </summary>
    public Func<Exception, ErrorAction>? ErrorPolicy { get; init; }

    /// <summary>
    /// Gets the optional circuit breaker notifier. When set, the error handling middleware
    /// reports success/failure outcomes so the circuit breaker can track the failure rate
    /// and pause/resume the consumer group via flow control.
    /// </summary>
    public ICircuitBreakerNotifier? CircuitBreakerNotifier { get; init; }

    /// <summary>
    /// Gets a value indicating whether the transactional outbox is enabled. When <see langword="true"/>
    /// and the consumer type is decorated with <see cref="TransactionalAttribute"/>, a
    /// <see cref="TransactionalOutboxMiddleware{TContext}"/> is inserted inside the retry loop.
    /// </summary>
    public bool OutboxEnabled { get; init; }

    /// <summary>
    /// Composes a complete consume pipeline for a single consumer or router entry.
    /// </summary>
    /// <param name="terminal">The innermost delegate that invokes the consumer handler.</param>
    /// <param name="perEntryPipeline">
    /// Optional middleware pipeline specific to this entry. Applied between handler and retry.
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

        // 1.5. Transactional middleware (wraps handler, inside retry loop)
        if (OutboxEnabled && consumerType is not null)
        {
            var transactionalMw = new TransactionalOutboxMiddleware<ConsumeContext<TValue>>(consumerType);
            terminal = new MiddlewarePipeline<ConsumeContext<TValue>>(transactionalMw, terminal);
        }

        // 2. Per-entry middleware
        if (perEntryPipeline is not null)
            terminal = perEntryPipeline.Build<ConsumeContext<TValue>, TValue>(Services, terminal);

        // 3. RetryMiddleware (if configured)
        if (RetryConfig is not null)
        {
            var retryMw = new RetryMiddleware<TValue>(
                RetryConfig,
                Services.GetRequiredService<EmitMetrics>(),
                Services.GetRequiredService<INodeIdentity>(),
                LoggerFactory.CreateLogger<RetryMiddleware<TValue>>());
            terminal = new MiddlewarePipeline<ConsumeContext<TValue>>(retryMw, terminal);
        }

        // 4. ValidationMiddleware (if configured) — outside retry so validation failures skip retry
        if (Validation is { IsConfigured: true })
        {
            var validationMw = new ValidationMiddleware<TValue>(
                Validation,
                Services.GetRequiredService<EmitMetrics>(),
                LoggerFactory.CreateLogger<ValidationMiddleware<TValue>>());
            terminal = new MiddlewarePipeline<ConsumeContext<TValue>>(validationMw, terminal);
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

        // 7. ConsumeTracingMiddleware (baked identity)
        {
            var tracingMw = new ConsumeTracingMiddleware<TValue>(
                Services.GetRequiredService<IOptions<EmitTracingOptions>>(),
                Services.GetRequiredService<ActivityEnricherInvoker>(),
                Services.GetRequiredService<INodeIdentity>(),
                identifier,
                consumerType);
            pipeline = new MiddlewarePipeline<ConsumeContext<TValue>>(tracingMw, pipeline);
        }

        // 8. ConsumeObserverMiddleware
        {
            var observerMw = new ConsumeObserverMiddleware<TValue>(
                ConsumeObservers,
                LoggerFactory.CreateLogger<ConsumeObserverMiddleware<TValue>>());
            pipeline = new MiddlewarePipeline<ConsumeContext<TValue>>(observerMw, pipeline);
        }

        // 9. ConsumeErrorMiddleware (outermost — catches everything, baked identity)
        {
            var errorMw = new ConsumeErrorMiddleware<TValue>(
                ErrorPolicy,
                Services.GetService<IDeadLetterSink>(),
                Services.GetRequiredService<EmitMetrics>(),
                Services.GetRequiredService<INodeIdentity>(),
                LoggerFactory.CreateLogger<ConsumeErrorMiddleware<TValue>>(),
                identifier,
                consumerType,
                CircuitBreakerNotifier);
            pipeline = new MiddlewarePipeline<ConsumeContext<TValue>>(errorMw, pipeline);
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

namespace Emit.Pipeline;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.Abstractions.Observability;
using Emit.Abstractions.Pipeline;
using Emit.Consumer;
using Emit.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Composes a fully-layered inbound middleware pipeline for a single consumer or router entry.
/// Layers are applied in this order, from innermost to outermost: validation, per-entry middleware,
/// error handling, and group/provider/global pipelines.
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
    /// Gets the ordered list of consume observers notified on processing errors.
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
    /// Gets the optional validation configuration. When set, a validation middleware is inserted
    /// as the innermost layer. When <see langword="null"/>, no validation is performed.
    /// </summary>
    public ConsumerValidation? GroupValidation { get; init; }

    /// <summary>
    /// Gets the optional error policy for the consumer group. When <see langword="null"/>, failed
    /// messages are discarded with a warning that identifies the consumer by name.
    /// </summary>
    public ErrorPolicy? GroupErrorPolicy { get; init; }

    /// <summary>
    /// Gets the optional dead letter destination resolver. Receives the source topic and returns
    /// the dead letter topic name, or <see langword="null"/> if no convention applies.
    /// </summary>
    public Func<string, string?>? ResolveDeadLetterDestination { get; init; }

    /// <summary>
    /// Gets the optional circuit breaker notifier. When set, the error handling middleware
    /// reports success/failure outcomes so the circuit breaker can track the failure rate
    /// and pause/resume the consumer group via flow control.
    /// </summary>
    public ICircuitBreakerNotifier? CircuitBreakerNotifier { get; init; }

    /// <summary>
    /// Composes a complete inbound pipeline for a single consumer or router entry by layering
    /// validation, per-entry middleware, error handling, and group/provider/global pipelines
    /// around the given terminal delegate.
    /// </summary>
    /// <param name="terminal">The innermost delegate that invokes the consumer handler.</param>
    /// <param name="perEntryPipeline">
    /// Optional middleware pipeline specific to this entry. Applied between validation and error handling.
    /// </param>
    /// <param name="identifier">
    /// The entry identifier used in error messages and observability. For direct consumers this is
    /// the handler type name; for routers this is the user-provided router identifier.
    /// </param>
    /// <param name="kind">Whether this entry is a direct consumer or a content-based router.</param>
    /// <param name="consumerType">
    /// The handler type for direct consumers, or <see langword="null"/> for routers.
    /// </param>
    /// <returns>
    /// A <see cref="ConsumerPipelineEntry{TValue}"/> pairing the composed pipeline with its identity metadata.
    /// </returns>
    public ConsumerPipelineEntry<TValue> Compose(
        MessageDelegate<InboundContext<TValue>> terminal,
        IMessagePipelineBuilder? perEntryPipeline,
        string identifier,
        ConsumerKind kind,
        Type? consumerType)
    {
        // Wrap with validation middleware (innermost layer, closest to handler)
        if (GroupValidation is not null)
        {
            var vm = new ValidationMiddleware<TValue>(
                GroupValidation,
                Services.GetService<IDeadLetterSink>(),
                ResolveDeadLetterDestination,
                Services.GetRequiredService<EmitMetrics>(),
                LoggerFactory.CreateLogger<ValidationMiddleware<TValue>>());
            var innerTerminal = terminal;
            terminal = ctx => vm.InvokeAsync(ctx, innerTerminal);
        }

        // If per-entry middleware exists, wrap the terminal with it
        if (perEntryPipeline is not null)
            terminal = perEntryPipeline.Build<InboundContext<TValue>, TValue>(Services, terminal);

        // Wrap with error handling middleware — always present.
        {
            string? unconfiguredName = GroupErrorPolicy is null ? identifier : null;
            var ehm = new ErrorHandlingMiddleware<TValue>(
                GroupErrorPolicy is not null ? GroupErrorPolicy.Evaluate : _ => ErrorAction.Discard(),
                Services.GetService<IDeadLetterSink>(),
                ResolveDeadLetterDestination,
                ConsumeObservers,
                Services.GetRequiredService<EmitMetrics>(),
                LoggerFactory.CreateLogger<ErrorHandlingMiddleware<TValue>>(),
                unconfiguredName,
                CircuitBreakerNotifier);
            var errorTerminal = terminal;
            terminal = ctx => ehm.InvokeAsync(ctx, errorTerminal);
        }

        // Then wrap with group → provider → global (outermost)
        var pipeline = GroupPipeline.Build<InboundContext<TValue>, TValue>(Services, terminal, GlobalInboundPipeline, ProviderInboundPipeline);

        return new ConsumerPipelineEntry<TValue>
        {
            Identifier = identifier,
            Kind = kind,
            ConsumerType = consumerType,
            Pipeline = pipeline,
        };
    }
}

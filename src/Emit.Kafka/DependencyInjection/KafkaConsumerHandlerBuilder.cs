namespace Emit.Kafka.DependencyInjection;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Pipeline;

/// <summary>
/// Configures per-consumer middleware for a single consumer handler registered
/// via <see cref="KafkaConsumerGroupBuilder{TKey, TValue}.AddConsumer{TConsumer}(Action{KafkaConsumerHandlerBuilder{TValue}})"/>.
/// Middleware registered here wraps only this consumer's invocation, running after
/// any group-level middleware.
/// </summary>
/// <typeparam name="TValue">The message value type.</typeparam>
public sealed class KafkaConsumerHandlerBuilder<TValue> : IInboundConfigurable<TValue>
{
    /// <inheritdoc />
    IMessagePipelineBuilder IInboundPipelineConfigurable.InboundPipeline => Pipeline;

    /// <summary>
    /// Gets the per-consumer middleware pipeline builder.
    /// </summary>
    internal IMessagePipelineBuilder Pipeline { get; } = new MessagePipelineBuilder();

    /// <inheritdoc />
    public IInboundConfigurable<TValue> Use<TMiddleware>(MiddlewareLifetime lifetime = default)
        where TMiddleware : class, IMiddleware<InboundContext<TValue>>
    {
        Pipeline.Use(typeof(TMiddleware), lifetime);
        return this;
    }

    /// <inheritdoc />
    public IInboundConfigurable<TValue> Filter<TFilter>()
        where TFilter : class, IConsumerFilter<TValue>
    {
        Pipeline.AddConsumerFilter<TValue, TFilter>();
        return this;
    }
}

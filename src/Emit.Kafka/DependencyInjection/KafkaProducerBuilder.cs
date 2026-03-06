namespace Emit.Kafka.DependencyInjection;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Pipeline;

/// <summary>
/// Configures per-producer outbound middleware for a topic.
/// </summary>
/// <typeparam name="TKey">The message key type.</typeparam>
/// <typeparam name="TValue">The message value type.</typeparam>
public sealed class KafkaProducerBuilder<TKey, TValue> : IOutboundConfigurable<TValue>
{
    /// <inheritdoc />
    IMessagePipelineBuilder IOutboundPipelineConfigurable.OutboundPipeline => Pipeline;

    /// <summary>
    /// Gets the per-producer outbound middleware pipeline builder. Middleware registered here
    /// wraps only this producer's outbound messages.
    /// </summary>
    internal IMessagePipelineBuilder Pipeline { get; } = new MessagePipelineBuilder();

    /// <summary>
    /// Creates a new producer builder.
    /// </summary>
    internal KafkaProducerBuilder()
    {
    }

    /// <inheritdoc />
    public IOutboundConfigurable<TValue> Use<TMiddleware>(MiddlewareLifetime lifetime = default)
        where TMiddleware : class, IMiddleware<OutboundContext<TValue>>
    {
        Pipeline.Use(typeof(TMiddleware), lifetime);
        return this;
    }
}

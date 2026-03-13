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
    /// Gets a value indicating whether this producer has opted out of the transactional outbox.
    /// </summary>
    internal bool DirectEnabled { get; private set; }

    /// <summary>
    /// Creates a new producer builder.
    /// </summary>
    internal KafkaProducerBuilder()
    {
    }

    /// <summary>
    /// Forces this producer to send messages directly to the broker, bypassing the outbox.
    /// When outbox infrastructure is configured, producers default to outbox routing.
    /// Call this method to opt out for producers where low-latency at-most-once delivery
    /// is preferred over transactional guarantees.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    /// <exception cref="InvalidOperationException">Called more than once on the same producer.</exception>
    public KafkaProducerBuilder<TKey, TValue> UseDirect()
    {
        if (DirectEnabled)
        {
            throw new InvalidOperationException(
                $"{nameof(UseDirect)} has already been called on this producer.");
        }

        DirectEnabled = true;
        return this;
    }

    /// <inheritdoc />
    public IOutboundConfigurable<TValue> Use<TMiddleware>(MiddlewareLifetime lifetime = default)
        where TMiddleware : class, IMiddleware<SendContext<TValue>>
    {
        Pipeline.Use(typeof(TMiddleware), lifetime);
        return this;
    }
}

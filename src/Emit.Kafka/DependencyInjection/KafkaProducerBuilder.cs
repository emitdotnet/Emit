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
    /// Gets a value indicating whether this producer has opted into the transactional outbox.
    /// </summary>
    internal bool OutboxEnabled { get; private set; }

    /// <summary>
    /// Creates a new producer builder.
    /// </summary>
    internal KafkaProducerBuilder()
    {
    }

    /// <summary>
    /// Routes this producer through the transactional outbox. Messages produced within a
    /// transaction are persisted to the outbox and delivered by the background worker after
    /// the transaction commits.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    /// <exception cref="InvalidOperationException">Called more than once on the same producer.</exception>
    public KafkaProducerBuilder<TKey, TValue> UseOutbox()
    {
        if (OutboxEnabled)
        {
            throw new InvalidOperationException(
                $"{nameof(UseOutbox)} has already been called on this producer.");
        }

        OutboxEnabled = true;
        return this;
    }

    /// <inheritdoc />
    public IOutboundConfigurable<TValue> Use<TMiddleware>(MiddlewareLifetime lifetime = default)
        where TMiddleware : class, IMiddleware<OutboundContext<TValue>>
    {
        Pipeline.Use(typeof(TMiddleware), lifetime);
        return this;
    }
}

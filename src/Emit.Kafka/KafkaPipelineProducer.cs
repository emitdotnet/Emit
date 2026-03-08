namespace Emit.Kafka;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Kafka.Consumer;
using Emit.Pipeline;

/// <summary>
/// Kafka producer that invokes the outbound middleware pipeline before serializing and producing.
/// Replaces direct <c>KafkaProducer</c> and <c>KafkaDirectProducer</c> — the terminal delegate
/// in the pipeline handles serialization and either outbox enqueue or direct Kafka produce.
/// </summary>
/// <typeparam name="TKey">The message key type.</typeparam>
/// <typeparam name="TValue">The message value type.</typeparam>
internal sealed class KafkaPipelineProducer<TKey, TValue>(
    MessageDelegate<OutboundContext<TValue>> pipeline,
    string topic,
    IServiceProvider services,
    TimeProvider timeProvider) : IEventProducer<TKey, TValue>
{
    /// <inheritdoc />
    public async Task ProduceAsync(EventMessage<TKey, TValue> message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var context = new OutboundContext<TValue>
        {
            MessageId = Guid.NewGuid().ToString(),
            Timestamp = timeProvider.GetUtcNow(),
            CancellationToken = cancellationToken,
            Services = services,
            Message = message.Value,
        };
        var keyFeature = new KeyFeature<TKey>(message.Key);
        context.Features.Set<IKeyFeature<TKey>>(keyFeature);
        context.Features.Set<IKeyTypeFeature>(keyFeature);
        var kafkaFeature = new KafkaFeature(topic, 0, 0);
        context.Features.Set<IMessageSourceFeature>(kafkaFeature);
        context.Features.Set<IKafkaFeature>(kafkaFeature);
        context.Features.Set<IHeadersFeature>(new HeadersFeature(message.Headers));

        await pipeline(context).ConfigureAwait(false);
    }
}

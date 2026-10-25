namespace Emit.Kafka;

using Emit.Abstractions;
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
    IMiddlewarePipeline<SendContext<TValue>> pipeline,
    Uri destinationAddress,
    Uri hostAddress,
    IServiceProvider services,
    TimeProvider timeProvider) : IEventProducer<TKey, TValue>
{
    /// <inheritdoc />
    public async Task ProduceAsync(EventMessage<TKey, TValue> message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageId = Guid.NewGuid().ToString();
        var timestamp = timeProvider.GetUtcNow();

        var context = new SendContext<TValue>
        {
            MessageId = messageId,
            Timestamp = timestamp,
            CancellationToken = cancellationToken,
            Services = services,
            Message = message.Value,
            DestinationAddress = destinationAddress,
            SourceAddress = hostAddress,
        };

        // The key travels to the terminal as a payload of the send context
        context.SetPayload(new KafkaMessageKey<TKey>(message.Key));

        // Copy user-provided headers to context
        if (message.Headers is { Count: > 0 })
        {
            context.Headers.AddRange(message.Headers);
        }

        // Every delivery of the message carries the same id, and where it was produced from; they replace those of
        // another message among the headers given, as when a handler forwards the headers of a message it consumed
        context.Headers.Upsert(WellKnownHeaders.MessageId, context.MessageId);
        context.Headers.Upsert(WellKnownHeaders.SourceAddress, hostAddress.ToString());

        await pipeline.InvokeAsync(context).ConfigureAwait(false);
    }
}

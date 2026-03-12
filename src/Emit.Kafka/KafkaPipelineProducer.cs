namespace Emit.Kafka;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Kafka.Consumer;

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

        var context = new SendContext<TValue>
        {
            MessageId = Guid.NewGuid().ToString(),
            Timestamp = timeProvider.GetUtcNow(),
            CancellationToken = cancellationToken,
            Services = services,
            Message = message.Value,
            DestinationAddress = destinationAddress,
            SourceAddress = hostAddress,
        };

        // Set Kafka transport context as payload so the terminal can extract the key
        context.SetPayload(new KafkaTransportContext<TKey>
        {
            Key = message.Key,
            MessageId = context.MessageId,
            Timestamp = context.Timestamp,
            CancellationToken = cancellationToken,
            Services = services,
            RawKey = null,
            RawValue = null,
            Headers = [],
            ProviderId = Provider.Identifier,
            Topic = EmitEndpointAddress.GetEntityName(destinationAddress) ?? "",
            Partition = -1,
            Offset = -1,
            GroupId = "",
            DestinationAddress = destinationAddress,
            SourceAddress = hostAddress,
        });

        // Copy user-provided headers to context
        if (message.Headers is { Count: > 0 })
        {
            context.Headers.AddRange(message.Headers);
        }

        await pipeline.InvokeAsync(context).ConfigureAwait(false);
    }
}

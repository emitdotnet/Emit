namespace Emit.Provider.Kafka;

using Emit.Abstractions;
using Emit.Models;
using Emit.Provider.Kafka.Serialization;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Kafka implementation of <see cref="IOutboxProvider"/> for processing outbox entries.
/// </summary>
/// <remarks>
/// <para>
/// This provider deserializes <see cref="KafkaPayload"/> from <see cref="OutboxEntry.Payload"/>
/// and sends the message to Kafka using the appropriate producer.
/// </para>
/// <para>
/// Producers are resolved by <see cref="OutboxEntry.RegistrationKey"/> using keyed DI services.
/// A fallback to the default registration ("__default__") is attempted if the exact key is not found.
/// </para>
/// </remarks>
internal sealed class KafkaOutboxProvider(
    IServiceProvider serviceProvider,
    ILogger<KafkaOutboxProvider> logger) : IOutboxProvider
{
    private readonly IServiceProvider serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly ILogger<KafkaOutboxProvider> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public string ProviderId => EmitConstants.Providers.Kafka;

    /// <inheritdoc/>
    public async Task ProcessAsync(OutboxEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        logger.LogDebug(
            "Processing outbox entry {EntryId} for Kafka: groupKey={GroupKey}, sequence={Sequence}",
            entry.Id, entry.GroupKey, entry.Sequence);

        // Step 1: Deserialize payload
        KafkaPayload payload;
        try
        {
            payload = MessagePackSerializer.Deserialize<KafkaPayload>(entry.Payload, cancellationToken: cancellationToken);
            logger.LogDebug(
                "Deserialized Kafka payload: topic={Topic}, hasKey={HasKey}, hasValue={HasValue}, hasHeaders={HasHeaders}",
                payload.Topic,
                payload.KeyBytes is not null,
                payload.ValueBytes is not null,
                payload.Headers is { Count: > 0 });
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to deserialize Kafka payload for entry {EntryId}: {Error}",
                entry.Id, ex.Message);
            throw new InvalidOperationException(
                $"Failed to deserialize Kafka payload for entry {entry.Id}. " +
                "The payload may be corrupted or in an incompatible format.", ex);
        }

        // Step 2: Resolve producer factory
        var factory = ResolveProducerFactory(entry.RegistrationKey);
        var producer = factory.GetProducer();

        logger.LogDebug(
            "Resolved Kafka producer for entry {EntryId}, registration key '{RegistrationKey}'",
            entry.Id, entry.RegistrationKey);

        // Step 3: Build message and produce to Kafka
        var message = BuildMessage(payload);

        try
        {
            var result = await producer.ProduceAsync(payload.Topic, message, cancellationToken)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Successfully produced message to Kafka: entryId={EntryId}, topic={Topic}, partition={Partition}, offset={Offset}",
                entry.Id, payload.Topic, result.Partition.Value, result.Offset.Value);
        }
        catch (ConfluentKafka.ProduceException<byte[], byte[]> ex)
        {
            logger.LogError(
                ex,
                "Kafka produce failed for entry {EntryId}: topic={Topic}, errorCode={ErrorCode}, reason={Reason}",
                entry.Id, payload.Topic, ex.Error.Code, ex.Error.Reason);
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Kafka produce cancelled for entry {EntryId}: topic={Topic}",
                entry.Id, payload.Topic);
            throw;
        }
    }

    private KafkaProducerFactory ResolveProducerFactory(string registrationKey)
    {
        // Try exact key match first
        var factory = serviceProvider.GetKeyedService<KafkaProducerFactory>(registrationKey);
        if (factory is not null)
        {
            return factory;
        }

        // Try fallback to default if not the default key
        if (registrationKey != EmitConstants.DefaultRegistrationKey)
        {
            factory = serviceProvider.GetKeyedService<KafkaProducerFactory>(EmitConstants.DefaultRegistrationKey);
            if (factory is not null)
            {
                logger.LogDebug(
                    "No producer factory found for key '{RegistrationKey}', using default factory",
                    registrationKey);
                return factory;
            }
        }

        logger.LogError(
            "No Kafka producer factory registered for key '{RegistrationKey}' and no default factory found",
            registrationKey);

        throw new InvalidOperationException(
            $"Producer factory not found for registration key '{registrationKey}'. " +
            $"Ensure AddKafka was called with this key or a default registration exists.");
    }

    private static ConfluentKafka.Message<byte[], byte[]> BuildMessage(KafkaPayload payload)
    {
        var message = new ConfluentKafka.Message<byte[], byte[]>
        {
            Key = payload.KeyBytes!,
            Value = payload.ValueBytes!
        };

        // Copy headers if present
        if (payload.Headers is { Count: > 0 })
        {
            message.Headers = new ConfluentKafka.Headers();
            foreach (var (key, value) in payload.Headers)
            {
                message.Headers.Add(key, value);
            }
        }

        // Set timestamp if present (TimestampType != 0 means timestamp was set)
        if (payload.TimestampUnixMs.HasValue && payload.TimestampType != 0)
        {
            message.Timestamp = new ConfluentKafka.Timestamp(
                payload.TimestampUnixMs.Value,
                (ConfluentKafka.TimestampType)payload.TimestampType);
        }

        return message;
    }
}

namespace Emit.Provider.Kafka;

using Confluent.Kafka;
using Emit.Abstractions;
using Emit.Models;
using Emit.Provider.Kafka.Serialization;
using Emit.Resilience;
using MessagePack;
using Microsoft.Extensions.Logging;
using Transactional.Abstractions;

/// <summary>
/// A Kafka producer implementation that enqueues messages to the transactional outbox.
/// </summary>
/// <typeparam name="TKey">The message key type.</typeparam>
/// <typeparam name="TValue">The message value type.</typeparam>
internal sealed class KafkaProducer<TKey, TValue>(
    IOutboxRepository repository,
    ISerializer<TKey>? keySerializer,
    ISerializer<TValue>? valueSerializer,
    string registrationKey,
    string clusterIdentifier,
    ResiliencePolicy resiliencePolicy,
    ILogger<KafkaProducer<TKey, TValue>> logger) : IProducer<TKey, TValue>
{
    private readonly IOutboxRepository repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly ISerializer<TKey>? keySerializer = keySerializer;
    private readonly ISerializer<TValue>? valueSerializer = valueSerializer;
    private readonly string registrationKey = registrationKey ?? throw new ArgumentNullException(nameof(registrationKey));
    private readonly string clusterIdentifier = clusterIdentifier ?? throw new ArgumentNullException(nameof(clusterIdentifier));
    private readonly ResiliencePolicy resiliencePolicy = resiliencePolicy ?? throw new ArgumentNullException(nameof(resiliencePolicy));
    private readonly ILogger<KafkaProducer<TKey, TValue>> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public async Task<DeliveryResult<TKey, TValue>> ProduceAsync(
        string topic,
        Message<TKey, TValue> message,
        CancellationToken cancellationToken = default)
    {
        return await ProduceAsync(topic, message, transaction: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously enqueues a message to the outbox within the specified transaction.
    /// </summary>
    /// <param name="topic">The target topic.</param>
    /// <param name="message">The message to produce.</param>
    /// <param name="transaction">The transaction context, or null for best-effort enqueue.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A delivery result indicating the message was accepted into the outbox.</returns>
    public async Task<DeliveryResult<TKey, TValue>> ProduceAsync(
        string topic,
        Message<TKey, TValue> message,
        ITransactionContext? transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(message);

        logger.LogTrace(
            "Serializing message for topic {Topic} with key type {KeyType} and value type {ValueType}",
            topic, typeof(TKey).Name, typeof(TValue).Name);

        // Serialize key and value
        var keyBytes = SerializeKey(message.Key, topic);
        var valueBytes = SerializeValue(message.Value, topic);

        logger.LogTrace(
            "Serialized key: {KeyLength} bytes, value: {ValueLength} bytes",
            keyBytes?.Length ?? 0, valueBytes?.Length ?? 0);

        // Create Kafka payload
        var payload = CreatePayload(topic, message, keyBytes, valueBytes);

        // Serialize payload to MessagePack
        var payloadBytes = MessagePackSerializer.Serialize(payload, cancellationToken: cancellationToken);

        logger.LogTrace("Serialized payload to MessagePack: {PayloadLength} bytes", payloadBytes.Length);

        // Create outbox entry
        var groupKey = CreateGroupKey(topic);
        var entry = new OutboxEntry
        {
            ProviderId = EmitConstants.Providers.Kafka,
            RegistrationKey = registrationKey,
            GroupKey = groupKey,
            Payload = payloadBytes,
            Properties = CreateProperties(topic, message)
        };

        // Get next sequence number and enqueue
        entry.Sequence = await repository.GetNextSequenceAsync(groupKey, transaction, cancellationToken)
            .ConfigureAwait(false);

        logger.LogDebug(
            "Enqueuing message to outbox: topic={Topic}, groupKey={GroupKey}, sequence={Sequence}, transaction={HasTransaction}",
            topic, groupKey, entry.Sequence, transaction is not null);

        await repository.EnqueueAsync(entry, transaction, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Message enqueued to outbox: topic={Topic}, sequence={Sequence}",
            topic, entry.Sequence);

        // Return delivery result indicating acceptance into outbox
        return CreateDeliveryResult(topic, message, entry);
    }

    /// <inheritdoc/>
    public void Produce(
        string topic,
        Message<TKey, TValue> message,
        Action<DeliveryReport<TKey, TValue>>? deliveryHandler = null)
    {
        Produce(topic, message, transaction: null, deliveryHandler);
    }

    /// <summary>
    /// Synchronously enqueues a message to the outbox within the specified transaction.
    /// </summary>
    /// <param name="topic">The target topic.</param>
    /// <param name="message">The message to produce.</param>
    /// <param name="transaction">The transaction context, or null for best-effort enqueue.</param>
    /// <param name="deliveryHandler">Optional callback invoked after enqueue.</param>
    public void Produce(
        string topic,
        Message<TKey, TValue> message,
        ITransactionContext? transaction,
        Action<DeliveryReport<TKey, TValue>>? deliveryHandler = null)
    {
        // Note: This blocks on the async operation. In the future, when ITransactionContext.OnCommitted
        // is available, the deliveryHandler should be registered to fire on commit instead.
        var result = ProduceAsync(topic, message, transaction, CancellationToken.None)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        // Fire callback immediately (future: fire on OnCommitted)
        if (deliveryHandler is not null)
        {
            var report = new DeliveryReport<TKey, TValue>
            {
                Topic = result.Topic,
                Partition = result.Partition,
                Offset = result.Offset,
                Message = result.Message,
                Status = result.Status,
                Timestamp = result.Timestamp
            };
            deliveryHandler(report);
        }
    }

    /// <summary>
    /// Extracts the cluster identifier from ProducerConfig.BootstrapServers.
    /// </summary>
    /// <param name="bootstrapServers">The bootstrap servers string (comma-separated).</param>
    /// <returns>A normalized cluster identifier.</returns>
    public static string ExtractClusterIdentifier(string? bootstrapServers)
    {
        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            return "unknown";
        }

        // Use first server as cluster identifier, normalized
        var firstServer = bootstrapServers.Split(',')[0].Trim();
        return string.IsNullOrWhiteSpace(firstServer) ? "unknown" : firstServer;
    }

    private byte[]? SerializeKey(TKey key, string topic)
    {
        if (key is null)
        {
            return null;
        }

        if (keySerializer is null)
        {
            throw new InvalidOperationException(
                $"Cannot serialize key of type {typeof(TKey).Name}: no serializer configured. " +
                "Configure a key serializer in the producer registration.");
        }

        var context = new SerializationContext(MessageComponentType.Key, topic);
        return keySerializer.Serialize(key, context);
    }

    private byte[]? SerializeValue(TValue value, string topic)
    {
        if (value is null)
        {
            return null;
        }

        if (valueSerializer is null)
        {
            throw new InvalidOperationException(
                $"Cannot serialize value of type {typeof(TValue).Name}: no serializer configured. " +
                "Configure a value serializer in the producer registration.");
        }

        var context = new SerializationContext(MessageComponentType.Value, topic);
        return valueSerializer.Serialize(value, context);
    }

    private static KafkaPayload CreatePayload(
        string topic,
        Message<TKey, TValue> message,
        byte[]? keyBytes,
        byte[]? valueBytes)
    {
        // Note: Message<TKey, TValue> doesn't have a Partition property.
        // Partition is determined by the actual Kafka producer when delivering.
        // If partition targeting is needed, it can be added via a future API extension.
        var payload = new KafkaPayload
        {
            Topic = topic,
            KeyBytes = keyBytes,
            ValueBytes = valueBytes,
            Partition = null
        };

        // Convert headers
        if (message.Headers is { Count: > 0 })
        {
            payload.Headers = [];
            foreach (var header in message.Headers)
            {
                payload.Headers[header.Key] = header.GetValueBytes();
            }
        }

        // Convert timestamp
        if (message.Timestamp.Type != TimestampType.NotAvailable)
        {
            payload.TimestampUnixMs = message.Timestamp.UnixTimestampMs;
            payload.TimestampType = (int)message.Timestamp.Type;
        }

        return payload;
    }

    private string CreateGroupKey(string topic) => $"{clusterIdentifier}:{topic}";

    private Dictionary<string, string> CreateProperties(string topic, Message<TKey, TValue> message)
    {
        return new Dictionary<string, string>
        {
            ["topic"] = topic,
            ["cluster"] = clusterIdentifier,
            ["keyType"] = typeof(TKey).FullName ?? typeof(TKey).Name,
            ["valueType"] = typeof(TValue).FullName ?? typeof(TValue).Name
        };
    }

    private static DeliveryResult<TKey, TValue> CreateDeliveryResult(
        string topic,
        Message<TKey, TValue> message,
        OutboxEntry entry)
    {
        // Note: Partition is set to 0 as a placeholder. The actual partition
        // will be determined when the message is delivered to Kafka.
        // The Offset is set to the outbox sequence number for tracking purposes.
        return new DeliveryResult<TKey, TValue>
        {
            Topic = topic,
            Partition = new Partition(0),
            Offset = new Offset(entry.Sequence),
            Message = message,
            Status = PersistenceStatus.Persisted,
            Timestamp = message.Timestamp.Type == TimestampType.NotAvailable
                ? new Timestamp(entry.EnqueuedAt)
                : message.Timestamp
        };
    }
}

namespace Emit.Kafka;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Kafka.Metrics;
using Emit.Kafka.Serialization;
using Emit.Models;
using Emit.Tracing;
using MessagePack;
using Microsoft.Extensions.Logging;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Kafka implementation of <see cref="IOutboxProvider"/> for processing outbox entries.
/// </summary>
/// <remarks>
/// <para>
/// This provider deserializes <see cref="KafkaPayload"/> from <see cref="OutboxEntry.Payload"/>
/// and sends the message to Kafka using the configured producer.
/// </para>
/// </remarks>
internal sealed class KafkaOutboxProvider(
    ConfluentKafka.IProducer<byte[], byte[]> producer,
    KafkaMetrics kafkaMetrics,
    ILogger<KafkaOutboxProvider> logger) : IOutboxProvider
{
    private static readonly ActivitySource OutboxActivitySource = new("Emit.Outbox", "1.0.0");
    private static readonly ActivitySource ProviderActivitySource = new("Emit.Provider.kafka", "1.0.0");

    private readonly ConfluentKafka.IProducer<byte[], byte[]> producer = producer ?? throw new ArgumentNullException(nameof(producer));
    private readonly KafkaMetrics kafkaMetrics = kafkaMetrics ?? throw new ArgumentNullException(nameof(kafkaMetrics));
    private readonly ILogger<KafkaOutboxProvider> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public string ProviderId => Provider.Identifier;

    /// <inheritdoc/>
    public async Task ProcessAsync(OutboxEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var processActivity = OutboxActivityHelper.StartProcessActivity(OutboxActivitySource, entry);

        // Step 1: Deserialize payload
        KafkaPayload payload;
        try
        {
            payload = MessagePackSerializer.Deserialize<KafkaPayload>(entry.Payload, cancellationToken: cancellationToken);
        }
        catch (MessagePackSerializationException ex)
        {
            processActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(
                ex,
                "Failed to deserialize Kafka payload for entry {EntryId}: {Error}",
                entry.Id, ex.Message);
            throw new InvalidOperationException(
                $"Failed to deserialize Kafka payload for entry {entry.Id}. " +
                "The payload may be corrupted or in an incompatible format.", ex);
        }

        // Step 2: Produce to Kafka
        var message = BuildMessage(payload);

        var messageSize = (payload.KeyBytes?.Length ?? 0) + (payload.ValueBytes?.Length ?? 0);
        kafkaMetrics.RecordProduceMessageSize(messageSize, payload.Topic);

        var start = Stopwatch.GetTimestamp();
        try
        {
            await producer
                .ProduceAsync(payload.Topic, message, cancellationToken)
                .ConfigureAwait(false);

            var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            kafkaMetrics.RecordProduceDuration(elapsed, payload.Topic, "success");
            kafkaMetrics.RecordProduceMessage(payload.Topic, "success");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            kafkaMetrics.RecordProduceDuration(elapsed, payload.Topic, "error");
            kafkaMetrics.RecordProduceMessage(payload.Topic, "error");
            throw;
        }
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
            message.Headers = [];
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

namespace Emit.Kafka;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Kafka.Metrics;
using Emit.Models;
using Emit.Tracing;
using Microsoft.Extensions.Logging;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Kafka implementation of <see cref="IOutboxProvider"/> for processing outbox entries.
/// </summary>
/// <remarks>
/// <para>
/// This provider reads the message body, headers, and properties directly from the
/// <see cref="OutboxEntry"/> and sends the message to Kafka using the configured producer.
/// </para>
/// </remarks>
internal sealed class KafkaOutboxProvider(
    ConfluentKafka.IProducer<byte[], byte[]> producer,
    KafkaMetrics kafkaMetrics,
    INodeIdentity nodeIdentity,
    ILogger<KafkaOutboxProvider> logger) : IOutboxProvider
{
    private static readonly ActivitySource OutboxActivitySource = new("Emit.Outbox", "1.0.0");

    private readonly ConfluentKafka.IProducer<byte[], byte[]> producer = producer ?? throw new ArgumentNullException(nameof(producer));
    private readonly KafkaMetrics kafkaMetrics = kafkaMetrics ?? throw new ArgumentNullException(nameof(kafkaMetrics));
    private readonly INodeIdentity nodeIdentity = nodeIdentity ?? throw new ArgumentNullException(nameof(nodeIdentity));
    private readonly ILogger<KafkaOutboxProvider> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public string SystemId => Provider.Identifier;

    /// <inheritdoc/>
    public async Task ProcessAsync(OutboxEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var processActivity = OutboxActivityHelper.StartProcessActivity(OutboxActivitySource, entry, nodeIdentity.NodeId);

        // Extract topic from destination URI
        var topic = EmitEndpointAddress.GetEntityName(new Uri(entry.Destination));
        if (string.IsNullOrEmpty(topic))
        {
            var errorMessage = $"Cannot extract topic from destination '{entry.Destination}' for entry {entry.Id}.";
            processActivity?.SetStatus(ActivityStatusCode.Error, errorMessage);
            logger.LogError("Cannot extract topic from destination '{Destination}' for entry {EntryId}", entry.Destination, entry.Id);
            throw new InvalidOperationException(errorMessage);
        }

        // Build the Kafka message directly from entry fields
        var message = BuildMessage(entry);

        var messageSize = (message.Key?.Length ?? 0) + (message.Value?.Length ?? 0);
        kafkaMetrics.RecordProduceMessageSize(messageSize, topic);

        var start = Stopwatch.GetTimestamp();
        try
        {
            await producer
                .ProduceAsync(topic, message, cancellationToken)
                .ConfigureAwait(false);

            var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            kafkaMetrics.RecordProduceDuration(elapsed, topic, "success");
            kafkaMetrics.RecordProduceMessage(topic, "success");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            kafkaMetrics.RecordProduceDuration(elapsed, topic, "error");
            kafkaMetrics.RecordProduceMessage(topic, "error");
            throw;
        }
    }

    private static ConfluentKafka.Message<byte[], byte[]> BuildMessage(OutboxEntry entry)
    {
        // Decode key from base64 in properties
        byte[]? keyBytes = null;
        if (entry.Properties.TryGetValue(OutboxPropertyKeys.Key, out var keyBase64))
        {
            keyBytes = Convert.FromBase64String(keyBase64);
        }

        var kafkaMessage = new ConfluentKafka.Message<byte[], byte[]>
        {
            Key = keyBytes!,
            Value = entry.Body!
        };

        // Copy headers if present
        if (entry.Headers is { Count: > 0 })
        {
            kafkaMessage.Headers = [];
            foreach (var (key, value) in entry.Headers)
            {
                kafkaMessage.Headers.Add(key, value);
            }
        }

        return kafkaMessage;
    }
}

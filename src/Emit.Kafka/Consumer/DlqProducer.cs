namespace Emit.Kafka.Consumer;

using System.Text;
using Emit.Abstractions;
using Emit.Kafka.DependencyInjection;
using Microsoft.Extensions.Logging;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Produces messages to Kafka dead letter topics using the shared <see cref="ConfluentKafka.IProducer{TKey,TValue}"/>
/// singleton. Retries transient failures with exponential backoff.
/// </summary>
internal sealed class DlqProducer(
    ConfluentKafka.IProducer<byte[], byte[]> producer,
    KafkaDeadLetterOptions options,
    ILogger<DlqProducer> logger) : IDeadLetterSink
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(16);

    /// <inheritdoc />
    public Uri DestinationAddress => options.DestinationAddress;

    /// <inheritdoc />
    public async Task ProduceAsync(
        byte[]? rawKey,
        byte[]? rawValue,
        IReadOnlyList<KeyValuePair<string, string>> headers,
        CancellationToken cancellationToken)
    {
        var kafkaHeaders = new ConfluentKafka.Headers();
        foreach (var header in headers)
        {
            kafkaHeaders.Add(header.Key, Encoding.UTF8.GetBytes(header.Value));
        }

        var message = new ConfluentKafka.Message<byte[], byte[]>
        {
            Key = rawKey!,
            Value = rawValue!,
            Headers = kafkaHeaders,
        };

        var topicName = options.TopicName;

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                await producer.ProduceAsync(topicName, message, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxAttempts - 1)
            {
                var delay = CalculateDelay(attempt);
                logger.LogWarning(ex,
                    "DLQ produce to {Topic} failed (attempt {Attempt}/{MaxAttempts}), retrying in {Delay}ms",
                    topicName, attempt + 1, MaxAttempts, (long)delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        // Final attempt — let exception propagate
        try
        {
            await producer.ProduceAsync(topicName, message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "DLQ produce to {Topic} failed after {MaxAttempts} attempts, message will not be committed",
                topicName, MaxAttempts);
            throw;
        }
    }

    private static TimeSpan CalculateDelay(int attempt)
    {
        var delayMs = InitialDelay.TotalMilliseconds * Math.Pow(2, attempt);
        return TimeSpan.FromMilliseconds(Math.Min(delayMs, MaxDelay.TotalMilliseconds));
    }
}

namespace Emit.Kafka.Consumer;

using System.Diagnostics;
using System.Text;
using Emit.Abstractions;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tracing;
using Emit.Tracing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Produces messages to Kafka dead letter topics using the shared <see cref="ConfluentKafka.IProducer{TKey,TValue}"/>
/// singleton. Retries transient failures with exponential backoff.
/// </summary>
internal sealed class DlqProducer(
    ConfluentKafka.IProducer<byte[], byte[]> producer,
    KafkaDeadLetterOptions options,
    IOptions<EmitTracingOptions> tracingOptions,
    INodeIdentity nodeIdentity,
    ILogger<DlqProducer> logger) : IDeadLetterSink
{

    private const int MaxAttempts = 5;
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(16);

    /// <inheritdoc />
    public Uri DestinationAddress => options.DestinationAddress;

    /// <inheritdoc />
    /// <remarks>
    /// The dead letter is a new message: the span of this send is its creation context. It is a child of the
    /// processing that failed, or of the failed message's creation when nothing processed it, as when it could not
    /// be deserialized.
    /// </remarks>
    public async Task ProduceAsync(
        byte[]? rawKey,
        byte[]? rawValue,
        IReadOnlyList<KeyValuePair<string, string>> headers,
        CancellationToken cancellationToken)
    {
        var tracing = tracingOptions.Value;
        var deadLetterHeaders = headers.ToList();
        using var activity = KafkaSendActivity.Start(
            EmitActivitySources.Consumer,
            tracing,
            ActivityKind.Producer,
            DestinationAddress,
            headers.GetFirstValue(WellKnownHeaders.MessageId),
            rawValue is null,
            Activity.Current is null ? MessagingActivity.CreationContext(headers) : default,
            [new(ActivityTagNames.NodeId, nodeIdentity.NodeId.ToString())]);

        if (activity is not null)
        {
            MessagingActivity.Inject(activity, deadLetterHeaders, tracing, logger);
        }

        var kafkaHeaders = new ConfluentKafka.Headers();
        foreach (var header in deadLetterHeaders)
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

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var result = await producer.ProduceAsync(topicName, message, cancellationToken).ConfigureAwait(false);
                KafkaSendActivity.Complete(activity, result.Partition, result.Offset);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                var delay = CalculateDelay(attempt - 1);
                logger.LogWarning(ex,
                    "DLQ produce to {Topic} failed (attempt {Attempt}/{MaxAttempts}), retrying in {Delay}ms",
                    topicName, attempt, MaxAttempts, (long)delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                KafkaSendActivity.Fail(activity, ex);
                logger.LogError(ex,
                    "DLQ produce to {Topic} failed after {MaxAttempts} attempts, message will not be committed",
                    topicName, MaxAttempts);
                throw;
            }
        }
    }

    private static TimeSpan CalculateDelay(int attempt)
    {
        var delayMs = InitialDelay.TotalMilliseconds * Math.Pow(2, attempt);
        return TimeSpan.FromMilliseconds(Math.Min(delayMs, MaxDelay.TotalMilliseconds));
    }
}

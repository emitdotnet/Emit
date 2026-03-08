namespace Emit.Kafka.Consumer;

using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.Abstractions.Pipeline;
using Emit.Kafka.Metrics;
using Emit.Kafka.Observability;
using Emit.Metrics;
using Emit.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// A single processing worker in the pool. Reads raw messages from its bounded channel,
/// deserializes, fans out to consumers via the pipeline, and reports completed offsets.
/// </summary>
/// <typeparam name="TKey">The message key type.</typeparam>
/// <typeparam name="TValue">The message value type.</typeparam>
internal sealed class ConsumerWorker<TKey, TValue>
{
    private readonly string id;
    private readonly Channel<ConfluentKafka.ConsumeResult<byte[], byte[]>> channel;
    private readonly ConsumerGroupRegistration<TKey, TValue> registration;
    private readonly IReadOnlyList<ConsumerPipelineEntry<TValue>> consumerPipelines;
    private readonly OffsetManager offsetManager;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly string groupId;
    private readonly KafkaConsumerObserverInvoker observerInvoker;
    private readonly KafkaMetrics kafkaMetrics;
    private readonly EmitMetrics emitMetrics;
    private readonly IDeadLetterSink? deadLetterSink;
    private readonly ILogger logger;
    private readonly string deserializationAction;

    /// <summary>
    /// The human-readable identifier for this worker, used in log messages.
    /// </summary>
    internal string Id => id;

    /// <summary>
    /// The bounded channel writer. The poll loop writes messages here.
    /// </summary>
    internal ChannelWriter<ConfluentKafka.ConsumeResult<byte[], byte[]>> Writer { get; }

    /// <summary>
    /// Current number of messages queued in the worker's bounded channel.
    /// </summary>
    internal int ChannelCount => channel.Reader.Count;

    /// <summary>
    /// Creates a new consumer worker with a bounded channel.
    /// </summary>
    public ConsumerWorker(
        string id,
        ConsumerGroupRegistration<TKey, TValue> registration,
        OffsetManager offsetManager,
        IServiceScopeFactory scopeFactory,
        string groupId,
        KafkaConsumerObserverInvoker observerInvoker,
        KafkaMetrics kafkaMetrics,
        EmitMetrics emitMetrics,
        IDeadLetterSink? deadLetterSink,
        ILogger logger)
    {
        this.id = id;
        this.registration = registration;
        this.consumerPipelines = registration.BuildConsumerPipelines();
        this.offsetManager = offsetManager;
        this.scopeFactory = scopeFactory;
        this.groupId = groupId;
        this.observerInvoker = observerInvoker;
        this.kafkaMetrics = kafkaMetrics;
        this.emitMetrics = emitMetrics;
        this.deadLetterSink = deadLetterSink;
        this.logger = logger;
        this.deserializationAction = registration.DeserializationErrorAction is ErrorAction.DeadLetterAction ? "dead_letter" : "discard";

        channel = Channel.CreateBounded<ConfluentKafka.ConsumeResult<byte[], byte[]>>(
            new BoundedChannelOptions(registration.BufferSize)
            {
                SingleWriter = true,
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait
            });
        Writer = channel.Writer;
    }

    /// <summary>
    /// Main processing loop.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var reader = channel.Reader;
        logger.LogDebug("{WorkerId} started, idle", id);
        try
        {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var raw))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        DeserializedMessage<TKey, TValue> deserialized;
                        try
                        {
                            deserialized = await DeserializeAsync(raw).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }
                        catch (Exception ex)
                        {
                            await HandleDeserializationErrorAsync(raw, ex, cancellationToken).ConfigureAwait(false);
                            offsetManager.MarkAsProcessed(raw.Topic, raw.Partition.Value, raw.Offset.Value);
                            continue;
                        }

                        await FanOutAsync(raw, deserialized, cancellationToken).ConfigureAwait(false);
                        offsetManager.MarkAsProcessed(raw.Topic, raw.Partition.Value, raw.Offset.Value);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "{WorkerId} error processing message from {Topic}[{Partition}]@{Offset}",
                            id, raw.Topic, raw.Partition.Value, raw.Offset.Value);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
    }

    /// <summary>
    /// Completes the channel writer, signaling no more messages will be written.
    /// </summary>
    public void Complete()
    {
        channel.Writer.TryComplete();
    }

    private async Task HandleDeserializationErrorAsync(
        ConfluentKafka.ConsumeResult<byte[], byte[]> raw,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var action = registration.DeserializationErrorAction;

        if (action is ErrorAction.DeadLetterAction deadLetter)
        {
            await DeadLetterDeserializationErrorAsync(raw, exception, deadLetter, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Discard or unconfigured — log warning and advance past the message
            logger.LogWarning(exception,
                "Discarding message from {Topic}[{Partition}]@{Offset} in group '{GroupId}' due to deserialization error",
                raw.Topic, raw.Partition.Value, raw.Offset.Value, groupId);
        }
    }

    private async Task DeadLetterDeserializationErrorAsync(
        ConfluentKafka.ConsumeResult<byte[], byte[]> raw,
        Exception exception,
        ErrorAction.DeadLetterAction deadLetter,
        CancellationToken cancellationToken)
    {
        if (deadLetterSink is null)
        {
            logger.LogError(exception,
                "Dead letter sink is not configured; cannot dead-letter deserialization failure from {Topic}[{Partition}]@{Offset} in group '{GroupId}'. Discarding message.",
                raw.Topic, raw.Partition.Value, raw.Offset.Value, groupId);
            return;
        }

        // Resolve DLQ topic: explicit override > convention > error
        var dlqTopic = deadLetter.TopicName;
        if (dlqTopic is null && registration.ResolveDeadLetterTopic is not null)
        {
            dlqTopic = registration.ResolveDeadLetterTopic(raw.Topic);
        }

        if (dlqTopic is null)
        {
            logger.LogError(exception,
                "Cannot resolve dead letter topic for deserialization failure from {Topic}[{Partition}]@{Offset} in group '{GroupId}'. Discarding message.",
                raw.Topic, raw.Partition.Value, raw.Offset.Value, groupId);
            return;
        }

        // Build headers: preserve originals + add diagnostic headers
        var headers = new List<KeyValuePair<string, string>>();

        if (raw.Message.Headers is not null)
        {
            foreach (var header in raw.Message.Headers)
            {
                headers.Add(new KeyValuePair<string, string>(
                    header.Key,
                    Encoding.UTF8.GetString(header.GetValueBytes())));
            }
        }

        headers.Add(new("x-emit-exception-type", exception.GetType().FullName ?? exception.GetType().Name));
        headers.Add(new("x-emit-exception-message", exception.Message));
        headers.Add(new("x-emit-consumer-group", groupId));
        headers.Add(new("x-emit-source-topic", raw.Topic));
        headers.Add(new("x-emit-source-partition", raw.Partition.Value.ToString()));
        headers.Add(new("x-emit-source-offset", raw.Offset.Value.ToString()));
        headers.Add(new("x-emit-timestamp", DateTimeOffset.UtcNow.ToString("o")));

        try
        {
            await deadLetterSink.ProduceAsync(
                raw.Message.Key,
                raw.Message.Value,
                headers,
                dlqTopic,
                cancellationToken).ConfigureAwait(false);

            kafkaMetrics.RecordDlqProduced(groupId, raw.Topic, dlqTopic);

            logger.LogWarning(exception,
                "Dead-lettered deserialization failure from {Topic}[{Partition}]@{Offset} in group '{GroupId}' to {DlqTopic}",
                raw.Topic, raw.Partition.Value, raw.Offset.Value, groupId, dlqTopic);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception dlqEx)
        {
            emitMetrics.RecordDlqProduceErrors("deserialization_error", dlqTopic);

            logger.LogError(dlqEx,
                "Failed to dead-letter deserialization failure from {Topic}[{Partition}]@{Offset} in group '{GroupId}' to {DlqTopic}. Discarding message.",
                raw.Topic, raw.Partition.Value, raw.Offset.Value, groupId, dlqTopic);
        }
    }

    private async Task<DeserializedMessage<TKey, TValue>> DeserializeAsync(
        ConfluentKafka.ConsumeResult<byte[], byte[]> raw)
    {
        var headers = raw.Message.Headers ?? new ConfluentKafka.Headers();

        TKey key;
        var start = Stopwatch.GetTimestamp();
        try
        {
            key = await KafkaSerializationHelper.DeserializeAsync(
                raw.Message.Key is not null ? new ReadOnlyMemory<byte>(raw.Message.Key) : ReadOnlyMemory<byte>.Empty,
                raw.Message.Key is null,
                raw.Topic,
                headers,
                registration.KeyDeserializer,
                registration.KeyAsyncDeserializer,
                ConfluentKafka.MessageComponentType.Key).ConfigureAwait(false);

            kafkaMetrics.RecordDeserializationDuration(Stopwatch.GetElapsedTime(start).TotalSeconds, groupId, "key");
        }
        catch (Exception ex)
        {
            kafkaMetrics.RecordDeserializationDuration(Stopwatch.GetElapsedTime(start).TotalSeconds, groupId, "key");
            kafkaMetrics.RecordDeserializationError(groupId, raw.Topic, "key", deserializationAction);
            await observerInvoker.OnDeserializationErrorAsync(new DeserializationErrorEvent(groupId, raw.Topic, raw.Partition.Value, raw.Offset.Value, ex)).ConfigureAwait(false);
            throw;
        }

        TValue value;
        start = Stopwatch.GetTimestamp();
        try
        {
            value = await KafkaSerializationHelper.DeserializeAsync(
                raw.Message.Value is not null ? new ReadOnlyMemory<byte>(raw.Message.Value) : ReadOnlyMemory<byte>.Empty,
                raw.Message.Value is null,
                raw.Topic,
                headers,
                registration.ValueDeserializer,
                registration.ValueAsyncDeserializer,
                ConfluentKafka.MessageComponentType.Value).ConfigureAwait(false);

            kafkaMetrics.RecordDeserializationDuration(Stopwatch.GetElapsedTime(start).TotalSeconds, groupId, "value");
        }
        catch (Exception ex)
        {
            kafkaMetrics.RecordDeserializationDuration(Stopwatch.GetElapsedTime(start).TotalSeconds, groupId, "value");
            kafkaMetrics.RecordDeserializationError(groupId, raw.Topic, "value", deserializationAction);
            await observerInvoker.OnDeserializationErrorAsync(new DeserializationErrorEvent(groupId, raw.Topic, raw.Partition.Value, raw.Offset.Value, ex)).ConfigureAwait(false);
            throw;
        }

        var contextHeaders = new List<KeyValuePair<string, string>>();
        foreach (var header in headers)
        {
            contextHeaders.Add(new KeyValuePair<string, string>(
                header.Key,
                Encoding.UTF8.GetString(header.GetValueBytes())));
        }

        return new DeserializedMessage<TKey, TValue>
        {
            Key = key,
            Value = value,
            Topic = raw.Topic,
            Partition = raw.Partition.Value,
            Offset = raw.Offset.Value,
            Headers = contextHeaders,
            Timestamp = raw.Message.Timestamp.Type != ConfluentKafka.TimestampType.NotAvailable
                ? raw.Message.Timestamp.UtcDateTime
                : null,
        };
    }

    private async Task FanOutAsync(
        ConfluentKafka.ConsumeResult<byte[], byte[]> raw,
        DeserializedMessage<TKey, TValue> deserialized,
        CancellationToken cancellationToken)
    {
        foreach (var entry in consumerPipelines)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var context = new InboundContext<TValue>
            {
                MessageId = Guid.NewGuid().ToString(),
                Timestamp = deserialized.Timestamp ?? DateTimeOffset.UtcNow,
                CancellationToken = cancellationToken,
                Services = scope.ServiceProvider,
                Message = deserialized.Value,
            };
            var keyFeature = new KeyFeature<TKey>(deserialized.Key);
            context.Features.Set<IKeyFeature<TKey>>(keyFeature);
            context.Features.Set<IKeyTypeFeature>(keyFeature);
            var kafkaFeature = new KafkaFeature(deserialized.Topic, deserialized.Partition, deserialized.Offset);
            context.Features.Set<IMessageSourceFeature>(kafkaFeature);
            context.Features.Set<IKafkaFeature>(kafkaFeature);
            context.Features.Set<IHeadersFeature>(new HeadersFeature(deserialized.Headers));
            context.Features.Set<IRawBytesFeature>(new RawBytesFeature(raw.Message.Key, raw.Message.Value));
            context.Features.Set<IConsumerIdentityFeature>(
                new ConsumerIdentityFeature(entry.Identifier, entry.Kind, entry.ConsumerType));
            await entry.Pipeline(context).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Intermediate record holding deserialized message components before context creation.
/// </summary>
internal sealed class DeserializedMessage<TKey, TValue>
{
    public required TKey Key { get; init; }
    public required TValue Value { get; init; }
    public required string Topic { get; init; }
    public required int Partition { get; init; }
    public required long Offset { get; init; }
    public required IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; }
    public DateTimeOffset? Timestamp { get; init; }
}

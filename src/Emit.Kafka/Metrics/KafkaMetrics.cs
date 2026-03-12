namespace Emit.Kafka.Metrics;

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Emit.Abstractions.Metrics;

/// <summary>
/// Instruments for the <c>Emit.Kafka</c> meter — Kafka-specific producer and consumer metrics.
/// </summary>
public sealed class KafkaMetrics
{
    private readonly EmitMetricsEnrichment enrichment;
    private readonly ConcurrentDictionary<string, ConsumerGroupGaugeState> consumerGroupRegistry = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaMetrics"/> class.
    /// </summary>
    /// <param name="meterFactory">Optional meter factory for managed meter lifetime. Falls back to an unmanaged meter when <c>null</c>.</param>
    /// <param name="enrichment">Static enrichment tags appended to all recordings.</param>
    public KafkaMetrics(IMeterFactory? meterFactory, EmitMetricsEnrichment enrichment)
    {
        ArgumentNullException.ThrowIfNull(enrichment);
        this.enrichment = enrichment;

        var meter = meterFactory?.Create(MeterNames.EmitKafka) ?? new Meter(MeterNames.EmitKafka);

        // Producer
        ProduceDuration = meter.CreateHistogram<double>(
            "emit.kafka.produce.duration", "s", "Kafka broker round-trip time for produce operations.");

        ProduceMessages = meter.CreateCounter<long>(
            "emit.kafka.produce.messages", "{message}", "Count of Kafka produce operations.");

        ProduceMessageSize = meter.CreateHistogram<int>(
            "emit.kafka.produce.message_size", "By", "Message size distribution (key + value bytes).");

        // Consumer
        ConsumeMessages = meter.CreateCounter<long>(
            "emit.kafka.consume.messages", "{message}", "Count of messages read from Kafka.");

        DeserializationErrors = meter.CreateCounter<long>(
            "emit.kafka.consume.deserialization.errors", "{error}", "Count of deserialization exceptions.");

        DeserializationDuration = meter.CreateHistogram<double>(
            "emit.kafka.consume.deserialization.duration", "s", "Deserializer execution time.");

        PartitionEvents = meter.CreateCounter<long>(
            "emit.kafka.consume.partition.events", "{event}", "Count of partition rebalance events.");

        OffsetCommits = meter.CreateCounter<long>(
            "emit.kafka.consume.offset.commits", "{commit}", "Count of offset commit operations.");

        WorkerFaults = meter.CreateCounter<long>(
            "emit.kafka.consume.worker.faults", "{fault}", "Count of worker fault events.");

        DlqProduced = meter.CreateCounter<long>(
            "emit.kafka.consume.dlq.produced", "{message}", "Count of deserialization errors sent to DLQ.");

        // Observable gauges
        meter.CreateObservableGauge(
            "emit.kafka.consume.worker.channel_depth",
            ObserveChannelDepths,
            "{message}",
            "Messages queued in worker bounded channels.");

        meter.CreateObservableGauge(
            "emit.kafka.consume.groups.active",
            () => new Measurement<int>(consumerGroupRegistry.Count, enrichment.CreateTags()),
            "{consumer}",
            "Number of active consumer groups.");

        meter.CreateObservableGauge(
            "emit.kafka.consume.worker_pool.size",
            ObserveWorkerPoolSizes,
            "{worker}",
            "Configured worker count per consumer group.");
    }

    // Producer instruments
    internal Histogram<double> ProduceDuration { get; }

    internal Counter<long> ProduceMessages { get; }

    internal Histogram<int> ProduceMessageSize { get; }

    // Consumer instruments
    internal Counter<long> ConsumeMessages { get; }

    internal Counter<long> DeserializationErrors { get; }

    internal Histogram<double> DeserializationDuration { get; }

    internal Counter<long> PartitionEvents { get; }

    internal Counter<long> OffsetCommits { get; }

    internal Counter<long> WorkerFaults { get; }

    internal Counter<long> DlqProduced { get; }

    // ── Producer recording methods ──

    internal void RecordProduceDuration(double seconds, string topic, string result)
    {
        var tags = enrichment.CreateTags([new("topic", topic), new("result", result)]);
        ProduceDuration.Record(seconds, tags);
    }

    internal void RecordProduceMessage(string topic, string result)
    {
        var tags = enrichment.CreateTags([new("topic", topic), new("result", result)]);
        ProduceMessages.Add(1, tags);
    }

    internal void RecordProduceMessageSize(int sizeBytes, string topic)
    {
        var tags = enrichment.CreateTags([new("topic", topic)]);
        ProduceMessageSize.Record(sizeBytes, tags);
    }

    // ── Consumer recording methods ──

    internal void RecordConsumeMessage(string consumerGroup, string topic, int partition)
    {
        var tags = enrichment.CreateTags([new("consumer_group", consumerGroup), new("topic", topic), new("partition", partition)]);
        ConsumeMessages.Add(1, tags);
    }

    internal void RecordDeserializationError(string consumerGroup, string topic, string component, string action)
    {
        var tags = enrichment.CreateTags([new("consumer_group", consumerGroup), new("topic", topic), new("component", component), new("action", action)]);
        DeserializationErrors.Add(1, tags);
    }

    internal void RecordDeserializationDuration(double seconds, string consumerGroup, string component)
    {
        var tags = enrichment.CreateTags([new("consumer_group", consumerGroup), new("component", component)]);
        DeserializationDuration.Record(seconds, tags);
    }

    internal void RecordPartitionEvent(string consumerGroup, string topic, string type, int partitionCount)
    {
        var tags = enrichment.CreateTags([new("consumer_group", consumerGroup), new("topic", topic), new("type", type)]);
        PartitionEvents.Add(partitionCount, tags);
    }

    internal void RecordOffsetCommit(string consumerGroup, string result)
    {
        var tags = enrichment.CreateTags([new("consumer_group", consumerGroup), new("result", result)]);
        OffsetCommits.Add(1, tags);
    }

    internal void RecordWorkerFault(string consumerGroup)
    {
        var tags = enrichment.CreateTags([new("consumer_group", consumerGroup)]);
        WorkerFaults.Add(1, tags);
    }

    internal void RecordDlqProduced(string consumerGroup, string sourceTopic)
    {
        var tags = enrichment.CreateTags([new("consumer_group", consumerGroup), new("source_topic", sourceTopic)]);
        DlqProduced.Add(1, tags);
    }

    // ── Consumer group gauge registry ──

    internal void RegisterConsumerGroup(string groupId, int workerCount, Func<int[]> getChannelDepths)
    {
        consumerGroupRegistry[groupId] = new ConsumerGroupGaugeState(workerCount, getChannelDepths);
    }

    internal void DeregisterConsumerGroup(string groupId)
    {
        consumerGroupRegistry.TryRemove(groupId, out _);
    }

    private IEnumerable<Measurement<int>> ObserveChannelDepths()
    {
        foreach (var (groupId, state) in consumerGroupRegistry)
        {
            var depths = state.GetChannelDepths();
            for (var i = 0; i < depths.Length; i++)
            {
                var tags = enrichment.CreateTags([new("consumer_group", groupId), new("worker_index", i)]);
                yield return new Measurement<int>(depths[i], tags);
            }
        }
    }

    private IEnumerable<Measurement<int>> ObserveWorkerPoolSizes()
    {
        foreach (var (groupId, state) in consumerGroupRegistry)
        {
            var tags = enrichment.CreateTags([new("consumer_group", groupId)]);
            yield return new Measurement<int>(state.WorkerCount, tags);
        }
    }

    private sealed record ConsumerGroupGaugeState(int WorkerCount, Func<int[]> GetChannelDepths);
}

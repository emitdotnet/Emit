namespace Emit.Kafka.Metrics;

using System.Collections.Frozen;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Emit.Abstractions.Metrics;

/// <summary>
/// Instruments for the <c>Emit.Kafka.Broker</c> meter — librdkafka client and broker statistics.
/// Parses the statistics JSON delivered by the <c>StatisticsHandler</c> callback and caches
/// the latest values for observable gauge/counter callbacks.
/// </summary>
public sealed class KafkaBrokerMetrics
{
    private readonly EmitMetricsEnrichment enrichment;

    // Single consolidated snapshot swapped atomically on each statistics callback.
    // All observable callbacks capture this reference once, guaranteeing point-in-time
    // consistency across all instruments within a single collection cycle.
    private volatile StatisticsSnapshot snapshot = new(
        new ClientSnapshot(0, 0),
        new ConsumerGroupSnapshot(null, 0, 0),
        FrozenDictionary<int, BrokerSnapshot>.Empty,
        FrozenDictionary<(string Topic, int Partition), long>.Empty);

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaBrokerMetrics"/> class.
    /// </summary>
    /// <param name="meterFactory">Optional meter factory for managed meter lifetime. Falls back to an unmanaged meter when <c>null</c>.</param>
    /// <param name="enrichment">Static enrichment tags appended to all recordings.</param>
    public KafkaBrokerMetrics(IMeterFactory? meterFactory, EmitMetricsEnrichment enrichment)
    {
        ArgumentNullException.ThrowIfNull(enrichment);
        this.enrichment = enrichment;

        var meter = meterFactory?.Create(MeterNames.EmitKafkaBroker) ?? new Meter(MeterNames.EmitKafkaBroker);

        // L. Broker Health — Essential

        // #46 emit.kafka.broker.request.rtt
        meter.CreateObservableGauge(
            "emit.kafka.broker.request.rtt",
            () => ObserveBrokerPercentiles(b => b.RttP50Us / 1_000_000.0, b => b.RttP95Us / 1_000_000.0, b => b.RttP99Us / 1_000_000.0),
            "s",
            "Broker round-trip time.");

        // #47 emit.kafka.broker.throttle.duration
        meter.CreateObservableGauge(
            "emit.kafka.broker.throttle.duration",
            () => ObserveBrokerMetric(b => b.ThrottleAvgMs / 1_000.0),
            "s",
            "Average broker throttle time.");

        // #48 emit.kafka.broker.outbuf.messages
        meter.CreateObservableGauge(
            "emit.kafka.broker.outbuf.messages",
            () => ObserveBrokerMetric(b => b.OutbufMsgCnt),
            "{message}",
            "Messages in outbound buffer.");

        // #49 emit.kafka.broker.waitresp.messages
        meter.CreateObservableGauge(
            "emit.kafka.broker.waitresp.messages",
            () => ObserveBrokerMetric(b => b.WaitrespMsgCnt),
            "{message}",
            "Messages awaiting response.");

        // #50 emit.kafka.broker.request.timeouts
        meter.CreateObservableCounter(
            "emit.kafka.broker.request.timeouts",
            () => ObserveBrokerMetric(b => b.ReqTimeouts),
            "{request}",
            "Cumulative timed-out requests.");

        // #51 emit.kafka.broker.tx.errors
        meter.CreateObservableCounter(
            "emit.kafka.broker.tx.errors",
            () => ObserveBrokerMetric(b => b.TxErrors),
            "{error}",
            "Cumulative transmission errors.");

        // #52 emit.kafka.broker.connection.state
        meter.CreateObservableGauge(
            "emit.kafka.broker.connection.state",
            ObserveBrokerConnectionState,
            description: "Broker connection state (1=UP, 0=DOWN, 2=INIT, 3=CONNECTING, 4=AUTH).");

        // M. Broker Performance — Recommended

        // #53 emit.kafka.broker.client.messages_queued
        meter.CreateObservableGauge(
            "emit.kafka.broker.client.messages_queued",
            () => { var s = snapshot; return new Measurement<long>(s.Client.MessagesQueued, enrichment.CreateTags()); },
            "{message}",
            "Total messages in all producer queues.");

        // #54 emit.kafka.broker.client.bytes_queued
        meter.CreateObservableGauge(
            "emit.kafka.broker.client.bytes_queued",
            () => { var s = snapshot; return new Measurement<long>(s.Client.BytesQueued, enrichment.CreateTags()); },
            "By",
            "Total size of all queued messages.");

        // #55 emit.kafka.broker.tx.bytes
        meter.CreateObservableCounter(
            "emit.kafka.broker.tx.bytes",
            () => ObserveBrokerMetric(b => b.TxBytes),
            "By",
            "Cumulative bytes transmitted to broker.");

        // #56 emit.kafka.broker.tx.retries
        meter.CreateObservableCounter(
            "emit.kafka.broker.tx.retries",
            () => ObserveBrokerMetric(b => b.TxRetries),
            "{retry}",
            "Cumulative transmission retries.");

        // #57 emit.kafka.broker.internal_latency
        meter.CreateObservableGauge(
            "emit.kafka.broker.internal_latency",
            () => ObserveBrokerPercentiles(b => b.IntLatencyP50Us, b => b.IntLatencyP95Us, b => b.IntLatencyP99Us),
            "us",
            "Internal queue latency before transmission.");

        // N. Consumer Lag & Group Health

        // #58 emit.kafka.broker.consumer.lag
        meter.CreateObservableGauge(
            "emit.kafka.broker.consumer.lag",
            ObserveConsumerLag,
            "{message}",
            "Consumer lag per topic-partition.");

        // #59 emit.kafka.broker.consumer_group.state
        meter.CreateObservableGauge(
            "emit.kafka.broker.consumer_group.state",
            ObserveConsumerGroupState,
            description: "Consumer group state.");

        // #60 emit.kafka.broker.consumer_group.rebalances
        meter.CreateObservableCounter(
            "emit.kafka.broker.consumer_group.rebalances",
            () => { var s = snapshot; return new Measurement<long>(s.ConsumerGroup.RebalanceCount, enrichment.CreateTags()); },
            "{rebalance}",
            "Cumulative rebalance count.");

        // O. Broker Connectivity

        // #61 emit.kafka.broker.connects
        meter.CreateObservableCounter(
            "emit.kafka.broker.connects",
            () => ObserveBrokerMetric(b => b.Connects),
            "{connection}",
            "Cumulative connection attempts.");

        // #62 emit.kafka.broker.disconnects
        meter.CreateObservableCounter(
            "emit.kafka.broker.disconnects",
            () => ObserveBrokerMetric(b => b.Disconnects),
            "{disconnection}",
            "Cumulative disconnections.");
    }

    /// <summary>
    /// Parses a librdkafka statistics JSON blob and caches the latest values
    /// for observable gauge/counter callbacks.
    /// </summary>
    /// <param name="json">The statistics JSON string from librdkafka.</param>
    internal void HandleStatistics(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Top-level client stats
            var client = new ClientSnapshot(
                MessagesQueued: GetInt64OrDefault(root, "msg_cnt"),
                BytesQueued: GetInt64OrDefault(root, "msg_size"));

            // Per-broker stats — empty when section absent so disappeared brokers are dropped
            var brokerDict = new Dictionary<int, BrokerSnapshot>();
            if (root.TryGetProperty("brokers", out var brokers))
            {
                foreach (var broker in brokers.EnumerateObject())
                {
                    var b = broker.Value;
                    var nodeId = GetInt32OrDefault(b, "nodeid");
                    if (nodeId == -1) continue; // Skip internal/bootstrap brokers

                    var stateStr = GetStringOrDefault(b, "state");

                    brokerDict[nodeId] = new BrokerSnapshot(
                        RttP50Us: GetNestedDoubleOrDefault(b, "rtt", "p50"),
                        RttP95Us: GetNestedDoubleOrDefault(b, "rtt", "p95"),
                        RttP99Us: GetNestedDoubleOrDefault(b, "rtt", "p99"),
                        ThrottleAvgMs: GetNestedDoubleOrDefault(b, "throttle", "avg"),
                        OutbufMsgCnt: GetInt32OrDefault(b, "outbuf_msg_cnt"),
                        WaitrespMsgCnt: GetInt32OrDefault(b, "waitresp_msg_cnt"),
                        ReqTimeouts: GetInt64OrDefault(b, "req_timeouts"),
                        TxErrors: GetInt64OrDefault(b, "txerrs"),
                        TxBytes: GetInt64OrDefault(b, "txbytes"),
                        TxRetries: GetInt64OrDefault(b, "txretries"),
                        IntLatencyP50Us: GetNestedDoubleOrDefault(b, "int_latency", "p50"),
                        IntLatencyP95Us: GetNestedDoubleOrDefault(b, "int_latency", "p95"),
                        IntLatencyP99Us: GetNestedDoubleOrDefault(b, "int_latency", "p99"),
                        Connects: GetInt64OrDefault(b, "connects"),
                        Disconnects: GetInt64OrDefault(b, "disconnects"),
                        State: stateStr,
                        StateValue: MapConnectionState(stateStr));
                }
            }

            // Per-topic-partition consumer lag — empty when section absent so revoked partitions are dropped
            var lagDict = new Dictionary<(string, int), long>();
            if (root.TryGetProperty("topics", out var topics))
            {
                foreach (var topic in topics.EnumerateObject())
                {
                    var topicName = topic.Name;
                    if (topic.Value.TryGetProperty("partitions", out var partitions))
                    {
                        foreach (var partition in partitions.EnumerateObject())
                        {
                            if (!int.TryParse(partition.Name, out var partitionId)) continue;
                            if (partitionId == -1) continue; // Skip aggregate partition

                            var lag = GetInt64OrDefault(partition.Value, "consumer_lag");
                            if (lag >= 0)
                            {
                                lagDict[(topicName, partitionId)] = lag;
                            }
                        }
                    }
                }
            }

            // Consumer group stats — default when section absent
            var consumerGroup = root.TryGetProperty("cgrp", out var cgrp)
                ? new ConsumerGroupSnapshot(
                    State: GetStringOrDefault(cgrp, "state"),
                    StateValue: MapGroupState(GetStringOrDefault(cgrp, "state")),
                    RebalanceCount: GetInt64OrDefault(cgrp, "rebalance_cnt"))
                : new ConsumerGroupSnapshot(null, 0, 0);

            // Single atomic swap — all callbacks see a consistent point-in-time snapshot
            snapshot = new StatisticsSnapshot(
                client,
                consumerGroup,
                brokerDict.ToFrozenDictionary(),
                lagDict.ToFrozenDictionary());
        }
        catch (JsonException)
        {
            // Silently ignore malformed JSON — the statistics handler should be resilient
        }
    }

    // ── Observable callback helpers ──
    // Each helper captures the snapshot reference once into a local variable,
    // guaranteeing consistency even if a swap occurs mid-iteration.

    private IEnumerable<Measurement<T>> ObserveBrokerMetric<T>(Func<BrokerSnapshot, T> selector) where T : struct
    {
        var s = snapshot;
        foreach (var (brokerId, broker) in s.Brokers)
        {
            yield return new Measurement<T>(selector(broker), enrichment.CreateTags([new("broker_id", brokerId)]));
        }
    }

    private IEnumerable<Measurement<double>> ObserveBrokerPercentiles(
        Func<BrokerSnapshot, double> p50Selector,
        Func<BrokerSnapshot, double> p95Selector,
        Func<BrokerSnapshot, double> p99Selector)
    {
        var s = snapshot;
        foreach (var (brokerId, broker) in s.Brokers)
        {
            yield return new Measurement<double>(p50Selector(broker), enrichment.CreateTags([new("broker_id", brokerId), new("percentile", "p50")]));
            yield return new Measurement<double>(p95Selector(broker), enrichment.CreateTags([new("broker_id", brokerId), new("percentile", "p95")]));
            yield return new Measurement<double>(p99Selector(broker), enrichment.CreateTags([new("broker_id", brokerId), new("percentile", "p99")]));
        }
    }

    private IEnumerable<Measurement<int>> ObserveBrokerConnectionState()
    {
        var s = snapshot;
        foreach (var (brokerId, broker) in s.Brokers)
        {
            var tags = enrichment.CreateTags([new("broker_id", brokerId), new("state", broker.State ?? "UNKNOWN")]);
            yield return new Measurement<int>(broker.StateValue, tags);
        }
    }

    private IEnumerable<Measurement<long>> ObserveConsumerLag()
    {
        var s = snapshot;
        foreach (var ((topic, partition), lag) in s.ConsumerLag)
        {
            yield return new Measurement<long>(lag, enrichment.CreateTags([new("topic", topic), new("partition", partition)]));
        }
    }

    private IEnumerable<Measurement<int>> ObserveConsumerGroupState()
    {
        var s = snapshot;
        if (s.ConsumerGroup.State is not null)
        {
            var tags = enrichment.CreateTags([new("state", s.ConsumerGroup.State)]);
            yield return new Measurement<int>(s.ConsumerGroup.StateValue, tags);
        }
    }

    // ── JSON helpers ──

    private static long GetInt64OrDefault(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt64()
            : 0;
    }

    private static int GetInt32OrDefault(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : 0;
    }

    private static string? GetStringOrDefault(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static double GetNestedDoubleOrDefault(JsonElement element, string parentProp, string childProp)
    {
        if (element.TryGetProperty(parentProp, out var parent) &&
            parent.TryGetProperty(childProp, out var child) &&
            child.ValueKind == JsonValueKind.Number)
        {
            return child.GetDouble();
        }

        return 0;
    }

    private static int MapConnectionState(string? state) => state?.ToUpperInvariant() switch
    {
        "UP" => 1,
        "DOWN" => 0,
        "INIT" => 2,
        "CONNECT" or "CONNECTING" => 3,
        "AUTH" => 4,
        _ => -1,
    };

    private static int MapGroupState(string? state) => state?.ToLowerInvariant() switch
    {
        "up" => 1,
        "preparingrebalance" or "preparing_rebalance" => 2,
        "completingrebalance" or "completing_rebalance" => 3,
        "stable" => 4,
        "dead" => 5,
        "empty" => 6,
        _ => 0,
    };

    // ── Snapshot types ──

    private sealed record StatisticsSnapshot(
        ClientSnapshot Client,
        ConsumerGroupSnapshot ConsumerGroup,
        FrozenDictionary<int, BrokerSnapshot> Brokers,
        FrozenDictionary<(string Topic, int Partition), long> ConsumerLag);

    private sealed record ClientSnapshot(long MessagesQueued, long BytesQueued);

    private sealed record ConsumerGroupSnapshot(string? State, int StateValue, long RebalanceCount);

    private readonly record struct BrokerSnapshot(
        double RttP50Us,
        double RttP95Us,
        double RttP99Us,
        double ThrottleAvgMs,
        int OutbufMsgCnt,
        int WaitrespMsgCnt,
        long ReqTimeouts,
        long TxErrors,
        long TxBytes,
        long TxRetries,
        double IntLatencyP50Us,
        double IntLatencyP95Us,
        double IntLatencyP99Us,
        long Connects,
        long Disconnects,
        string? State,
        int StateValue);
}

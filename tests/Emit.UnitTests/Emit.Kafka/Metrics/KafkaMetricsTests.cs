namespace Emit.Kafka.Tests.Metrics;

using System.Diagnostics.Metrics;
using global::Emit.Abstractions.Metrics;
using global::Emit.Kafka.Metrics;
using Xunit;

[Collection("MetricsTests")]
public sealed class KafkaMetricsTests : IDisposable
{
    private readonly MeterListener listener = new();
    private readonly List<(double value, KeyValuePair<string, object?>[] tags)> histogramDoubles = [];
    private readonly List<(int value, KeyValuePair<string, object?>[] tags)> histogramInts = [];
    private readonly List<(long value, KeyValuePair<string, object?>[] tags)> counterMeasurements = [];
    private readonly List<(int value, KeyValuePair<string, object?>[] tags)> gaugeMeasurements = [];

    public KafkaMetricsTests()
    {
        listener.InstrumentPublished = (instrument, listenerInstance) =>
        {
            if (instrument.Meter.Name == MeterNames.EmitKafka)
            {
                listenerInstance.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
        {
            histogramDoubles.Add((measurement, tags.ToArray()));
        });

        listener.SetMeasurementEventCallback<int>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name == "emit.kafka.produce.message_size")
            {
                histogramInts.Add((measurement, tags.ToArray()));
            }
            else
            {
                gaugeMeasurements.Add((measurement, tags.ToArray()));
            }
        });

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            counterMeasurements.Add((measurement, tags.ToArray()));
        });

        listener.Start();
    }

    public void Dispose()
    {
        listener.Dispose();
    }

    [Fact]
    public void GivenProduceDuration_WhenRecorded_ThenEmitsMeasurementWithTopicAndResultTags()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment(new KeyValuePair<string, object?>[] { new("env", "test") });
        var metrics = new KafkaMetrics(null, enrichment);

        // Act
        metrics.RecordProduceDuration(1.5, "my-topic", "success");

        // Assert
        var measurement = Assert.Single(histogramDoubles);
        Assert.Equal(1.5, measurement.value);
        var tags = measurement.tags;
        Assert.Equal(3, tags.Length);
        Assert.Contains(tags, tag => tag.Key == "env" && Equals(tag.Value, "test"));
        Assert.Contains(tags, tag => tag.Key == "topic" && Equals(tag.Value, "my-topic"));
        Assert.Contains(tags, tag => tag.Key == "result" && Equals(tag.Value, "success"));
    }

    [Fact]
    public void GivenProduceMessage_WhenRecorded_ThenEmitsCounterWithTopicAndResultTags()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var metrics = new KafkaMetrics(null, enrichment);

        // Act
        metrics.RecordProduceMessage("events", "error");

        // Assert
        var measurement = Assert.Single(counterMeasurements);
        Assert.Equal(1, measurement.value);
        var tags = measurement.tags;
        Assert.Equal(2, tags.Length);
        Assert.Contains(tags, tag => tag.Key == "topic" && Equals(tag.Value, "events"));
        Assert.Contains(tags, tag => tag.Key == "result" && Equals(tag.Value, "error"));
    }

    [Fact]
    public void GivenProduceMessageSize_WhenRecorded_ThenEmitsHistogramWithTopicTag()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment(new KeyValuePair<string, object?>[] { new("app", "myapp") });
        var metrics = new KafkaMetrics(null, enrichment);

        // Act
        metrics.RecordProduceMessageSize(2048, "logs");

        // Assert
        var measurement = Assert.Single(histogramInts);
        Assert.Equal(2048, measurement.value);
        var tags = measurement.tags;
        Assert.Equal(2, tags.Length);
        Assert.Contains(tags, tag => tag.Key == "app" && Equals(tag.Value, "myapp"));
        Assert.Contains(tags, tag => tag.Key == "topic" && Equals(tag.Value, "logs"));
    }

    [Fact]
    public void GivenEnrichmentTags_WhenRecordingAnyMetric_ThenEnrichmentTagsAreAppended()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment(new KeyValuePair<string, object?>[]
        {
            new("service", "api"),
            new("version", "1.0.0")
        });
        var metrics = new KafkaMetrics(null, enrichment);

        // Act
        metrics.RecordOffsetCommit("test-group", "success");

        // Assert
        var measurement = Assert.Single(counterMeasurements);
        var tags = measurement.tags;
        Assert.Equal(4, tags.Length);
        Assert.Contains(tags, tag => tag.Key == "service" && Equals(tag.Value, "api"));
        Assert.Contains(tags, tag => tag.Key == "version" && Equals(tag.Value, "1.0.0"));
        Assert.Contains(tags, tag => tag.Key == "consumer_group" && Equals(tag.Value, "test-group"));
        Assert.Contains(tags, tag => tag.Key == "result" && Equals(tag.Value, "success"));
    }

    [Fact]
    public void GivenConsumerGroupRegistered_WhenObserveChannelDepths_ThenEmitsGaugesForEachWorker()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment(new KeyValuePair<string, object?>[] { new("region", "us-east") });
        var metrics = new KafkaMetrics(null, enrichment);
        var depths = new int[] { 10, 25, 0 };

        // Act
        metrics.RegisterConsumerGroup("declaration-1", "group1", "orders", 3, () => depths);
        listener.RecordObservableInstruments();

        // Assert — filter by specific consumer group to avoid leaking from other test meter instances
        var channelDepthMeasurements = gaugeMeasurements.Where(m =>
            m.tags.Any(t => t.Key == "worker_index") &&
            m.tags.Any(t => t.Key == "consumer_group" && Equals(t.Value, "group1"))).ToList();

        Assert.Equal(3, channelDepthMeasurements.Count);

        var worker0 = channelDepthMeasurements.Single(m => m.tags.Any(t => t.Key == "worker_index" && Equals(t.Value, 0)));
        Assert.Equal(10, worker0.value);
        Assert.Contains(worker0.tags, tag => tag.Key == "consumer_group" && Equals(tag.Value, "group1"));
        Assert.Contains(worker0.tags, tag => tag.Key == "topic" && Equals(tag.Value, "orders"));
        Assert.Contains(worker0.tags, tag => tag.Key == "region" && Equals(tag.Value, "us-east"));
    }

    [Fact]
    public void GivenTwoDeclarationsOnTheSameGroupId_WhenOneIsDeregistered_ThenTheOtherStillReportsItsChannelDepths()
    {
        // Arrange: the same group id consuming two topics is two consumer group declarations
        var enrichment = new EmitMetricsEnrichment();
        var metrics = new KafkaMetrics(null, enrichment);
        var groupId = $"group-{Guid.NewGuid():N}";

        metrics.RegisterConsumerGroup("orders-declaration", groupId, "orders", 2, () => [5, 10]);
        metrics.RegisterConsumerGroup("payments-declaration", groupId, "payments", 2, () => [15, 20]);

        // Act
        metrics.DeregisterConsumerGroup("orders-declaration");
        listener.RecordObservableInstruments();

        // Assert: filter by the group id of this test only
        var channelDepthMeasurements = gaugeMeasurements.Where(m =>
            m.tags.Any(t => t.Key == "worker_index") &&
            m.tags.Any(t => t.Key == "consumer_group" && Equals(t.Value, groupId))).ToList();

        Assert.Equal([15, 20], channelDepthMeasurements.Select(m => m.value).Order());
        Assert.All(channelDepthMeasurements, m =>
            Assert.Contains(m.tags, tag => tag.Key == "topic" && Equals(tag.Value, "payments")));
    }
}

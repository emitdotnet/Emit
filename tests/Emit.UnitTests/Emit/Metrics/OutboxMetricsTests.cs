namespace Emit.Tests.Metrics;

using System.Diagnostics.Metrics;
using global::Emit.Abstractions.Metrics;
using global::Emit.Metrics;
using Xunit;

[Collection("MetricsTests")]
public sealed class OutboxMetricsTests
{
    [Fact]
    public void GivenNullEnrichment_WhenConstructing_ThenThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new OutboxMetrics(null, null!));
    }

    [Fact]
    public void GivenEnqueued_WhenRecording_ThenMeasurementHasCorrectNameAndTags()
    {
        // Arrange
        var metrics = new OutboxMetrics(null, new EmitMetricsEnrichment());
        using var listener = CreateListener(out var longCapture, out _, out _);

        // Act
        metrics.RecordEnqueued("kafka");

        // Assert
        var m = Assert.Single(longCapture);
        Assert.Equal("emit.outbox.enqueued", m.Name);
        Assert.Equal(1L, m.Value);
        Assert.Contains(m.Tags, t => t.Key == "system" && t.Value?.ToString() == "kafka");
    }

    [Fact]
    public void GivenProcessingDuration_WhenRecording_ThenMeasurementHasCorrectNameAndTags()
    {
        // Arrange
        var metrics = new OutboxMetrics(null, new EmitMetricsEnrichment());
        using var listener = CreateListener(out _, out var doubleCapture, out _);

        // Act
        metrics.RecordProcessingDuration(1.5, "kafka", "success");

        // Assert
        var m = Assert.Single(doubleCapture);
        Assert.Equal("emit.outbox.processing.duration", m.Name);
        Assert.Equal(1.5, m.Value);
        Assert.Contains(m.Tags, t => t.Key == "system" && t.Value?.ToString() == "kafka");
        Assert.Contains(m.Tags, t => t.Key == "result" && t.Value?.ToString() == "success");
    }

    [Fact]
    public void GivenProcessingCompleted_WhenRecording_ThenMeasurementHasCorrectNameAndTags()
    {
        // Arrange
        var metrics = new OutboxMetrics(null, new EmitMetricsEnrichment());
        using var listener = CreateListener(out var longCapture, out _, out _);

        // Act
        metrics.RecordProcessingCompleted("kafka", "error");

        // Assert
        var m = Assert.Single(longCapture);
        Assert.Equal("emit.outbox.processing.completed", m.Name);
        Assert.Equal(1L, m.Value);
        Assert.Contains(m.Tags, t => t.Key == "system" && t.Value?.ToString() == "kafka");
        Assert.Contains(m.Tags, t => t.Key == "result" && t.Value?.ToString() == "error");
    }

    [Fact]
    public void GivenCriticalTime_WhenRecording_ThenMeasurementHasCorrectNameAndTags()
    {
        // Arrange
        var metrics = new OutboxMetrics(null, new EmitMetricsEnrichment());
        using var listener = CreateListener(out _, out var doubleCapture, out _);

        // Act
        metrics.RecordCriticalTime(5.2, "kafka");

        // Assert
        var m = Assert.Single(doubleCapture);
        Assert.Equal("emit.outbox.critical_time", m.Name);
        Assert.Equal(5.2, m.Value);
        Assert.Contains(m.Tags, t => t.Key == "system" && t.Value?.ToString() == "kafka");
    }

    [Fact]
    public void GivenPollCycle_WhenRecording_ThenMeasurementHasCorrectNameAndTags()
    {
        // Arrange
        var metrics = new OutboxMetrics(null, new EmitMetricsEnrichment());
        using var listener = CreateListener(out var longCapture, out _, out _);

        // Act
        metrics.RecordPollCycle(true);

        // Assert
        var m = Assert.Single(longCapture);
        Assert.Equal("emit.outbox.worker.poll_cycles", m.Name);
        Assert.Equal(1L, m.Value);
        Assert.Contains(m.Tags, t => t.Key == "has_entries" && t.Value?.ToString() == "true");
    }

    [Fact]
    public void GivenBatchEntries_WhenRecording_ThenMeasurementHasCorrectNameAndValue()
    {
        // Arrange
        var metrics = new OutboxMetrics(null, new EmitMetricsEnrichment());
        using var listener = CreateListener(out _, out _, out var intCapture);

        // Act
        metrics.RecordBatchEntries(42);

        // Assert
        var m = Assert.Single(intCapture);
        Assert.Equal("emit.outbox.worker.batch_entries", m.Name);
        Assert.Equal(42, m.Value);
    }

    [Fact]
    public void GivenWorkerError_WhenRecording_ThenMeasurementHasCorrectName()
    {
        // Arrange
        var metrics = new OutboxMetrics(null, new EmitMetricsEnrichment());
        using var listener = CreateListener(out var longCapture, out _, out _);

        // Act
        metrics.RecordWorkerError();

        // Assert
        var m = Assert.Single(longCapture);
        Assert.Equal("emit.outbox.worker.errors", m.Name);
        Assert.Equal(1L, m.Value);
    }

    [Fact]
    public void GivenActiveGroupsCallback_WhenObserved_ThenGaugeReportsCallbackValue()
    {
        // Arrange
        var metrics = new OutboxMetrics(null, new EmitMetricsEnrichment());
        var capturedValue = -1;

        metrics.RegisterActiveGroupsCallback(() => 7);

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == MeterNames.EmitOutbox && instrument.Name == "emit.outbox.worker.active_groups")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
        {
            capturedValue = value;
        });
        listener.Start();

        // Act
        listener.RecordObservableInstruments();

        // Assert
        Assert.Equal(7, capturedValue);
    }

    [Fact]
    public void GivenEnrichmentTags_WhenRecording_ThenEnrichmentTagsAreAppended()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment(new KeyValuePair<string, object?>[]
        {
            new("service", "api"),
            new("env", "prod")
        });
        var metrics = new OutboxMetrics(null, enrichment);
        using var listener = CreateListener(out var longCapture, out _, out _);

        // Act
        metrics.RecordEnqueued("kafka");

        // Assert
        var m = Assert.Single(longCapture);
        Assert.Equal(3, m.Tags.Length);
        Assert.Contains(m.Tags, t => t.Key == "service" && t.Value?.ToString() == "api");
        Assert.Contains(m.Tags, t => t.Key == "env" && t.Value?.ToString() == "prod");
        Assert.Contains(m.Tags, t => t.Key == "system" && t.Value?.ToString() == "kafka");
    }

    private static MeterListener CreateListener(
        out List<(string Name, long Value, KeyValuePair<string, object?>[] Tags)> longCapture,
        out List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)> doubleCapture,
        out List<(string Name, int Value, KeyValuePair<string, object?>[] Tags)> intCapture)
    {
        var longs = new List<(string Name, long Value, KeyValuePair<string, object?>[] Tags)>();
        var doubles = new List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)>();
        var ints = new List<(string Name, int Value, KeyValuePair<string, object?>[] Tags)>();

        longCapture = longs;
        doubleCapture = doubles;
        intCapture = ints;

        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == MeterNames.EmitOutbox)
                l.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            longs.Add((instrument.Name, value, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            doubles.Add((instrument.Name, value, tags.ToArray())));
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
            ints.Add((instrument.Name, value, tags.ToArray())));

        listener.Start();
        return listener;
    }
}

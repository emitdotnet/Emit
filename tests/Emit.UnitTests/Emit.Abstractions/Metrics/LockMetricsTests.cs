namespace Emit.Abstractions.Tests.Metrics;

using System.Diagnostics.Metrics;
using global::Emit.Abstractions.Metrics;
using Xunit;

[Collection("MetricsTests")]
public sealed class LockMetricsTests : IDisposable
{
    private readonly MeterListener listener = new();
    private readonly List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)> doubleMeasurements = [];
    private readonly List<(string Name, int Value, KeyValuePair<string, object?>[] Tags)> intMeasurements = [];
    private readonly List<(string Name, long Value, KeyValuePair<string, object?>[] Tags)> longMeasurements = [];

    public LockMetricsTests()
    {
        listener.InstrumentPublished = (instrument, listenerInstance) =>
        {
            if (instrument.Meter.Name == MeterNames.EmitLock)
            {
                listenerInstance.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            doubleMeasurements.Add((instrument.Name, value, tags.ToArray())));

        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
            intMeasurements.Add((instrument.Name, value, tags.ToArray())));

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            longMeasurements.Add((instrument.Name, value, tags.ToArray())));

        listener.Start();
    }

    public void Dispose()
    {
        listener.Dispose();
    }

    [Fact]
    public void GivenNullEnrichment_WhenConstructing_ThenThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new LockMetrics(null, null!));
    }

    [Fact]
    public void GivenAcquireDuration_WhenRecording_ThenMeasurementHasCorrectNameAndTags()
    {
        // Arrange
        var metrics = new LockMetrics(null, new EmitMetricsEnrichment());

        // Act
        metrics.RecordAcquireDuration(0.250, "success");

        // Assert
        var m = Assert.Single(doubleMeasurements);
        Assert.Equal("emit.lock.acquire.duration", m.Name);
        Assert.Equal(0.250, m.Value);
        Assert.Contains(m.Tags, t => t.Key == "result" && t.Value?.ToString() == "success");
    }

    [Fact]
    public void GivenAcquireRetries_WhenRecording_ThenMeasurementHasCorrectNameAndValue()
    {
        // Arrange
        var metrics = new LockMetrics(null, new EmitMetricsEnrichment());

        // Act
        metrics.RecordAcquireRetries(5);

        // Assert
        var m = Assert.Single(intMeasurements);
        Assert.Equal("emit.lock.acquire.retries", m.Name);
        Assert.Equal(5, m.Value);
    }

    [Fact]
    public void GivenRenewalCompleted_WhenRecording_ThenMeasurementHasCorrectNameAndTags()
    {
        // Arrange
        var metrics = new LockMetrics(null, new EmitMetricsEnrichment());

        // Act
        metrics.RecordRenewalCompleted("success");

        // Assert
        var m = Assert.Single(longMeasurements);
        Assert.Equal("emit.lock.renewal.completed", m.Name);
        Assert.Equal(1L, m.Value);
        Assert.Contains(m.Tags, t => t.Key == "result" && t.Value?.ToString() == "success");
    }

    [Fact]
    public void GivenHeldDuration_WhenRecording_ThenMeasurementHasCorrectNameAndValue()
    {
        // Arrange
        var metrics = new LockMetrics(null, new EmitMetricsEnrichment());

        // Act
        metrics.RecordHeldDuration(12.5);

        // Assert
        var m = Assert.Single(doubleMeasurements);
        Assert.Equal("emit.lock.held.duration", m.Name);
        Assert.Equal(12.5, m.Value);
    }

    [Fact]
    public void GivenEnrichmentTags_WhenRecording_ThenEnrichmentTagsAreAppended()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment(new KeyValuePair<string, object?>[]
        {
            new("service", "my-svc"),
            new("env", "staging")
        });
        var metrics = new LockMetrics(null, enrichment);

        // Act
        metrics.RecordAcquireDuration(0.1, "success");

        // Assert
        var m = Assert.Single(doubleMeasurements);
        Assert.Equal(3, m.Tags.Length);
        Assert.Contains(m.Tags, t => t.Key == "service" && t.Value?.ToString() == "my-svc");
        Assert.Contains(m.Tags, t => t.Key == "env" && t.Value?.ToString() == "staging");
        Assert.Contains(m.Tags, t => t.Key == "result" && t.Value?.ToString() == "success");
    }
}

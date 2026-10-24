namespace Emit.Tests.Metrics;

using System.Diagnostics.Metrics;
using global::Emit.Abstractions.Metrics;
using global::Emit.Metrics;
using Xunit;

[Collection("MetricsTests")]
public sealed class EmitMetricsTests
{
    [Fact]
    public void GivenNullEnrichment_WhenConstructing_ThenThrowsArgumentNullException()
    {
        // Arrange
        EmitMetricsEnrichment enrichment = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new EmitMetrics(null, enrichment));
    }

    [Fact]
    public void GivenProduceCompleted_WhenRecording_ThenRecordsDurationAndCounterWithCorrectTags()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var metrics = new EmitMetrics(null, enrichment);
        var listener = new MeterListener();
        var capturedDoubles = new List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)>();
        var capturedLongs = new List<(string Name, long Value, KeyValuePair<string, object?>[] Tags)>();

        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterNames.Emit)
                listener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
            capturedDoubles.Add((instrument.Name, value, tags.ToArray())));
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            capturedLongs.Add((instrument.Name, value, tags.ToArray())));

        listener.Start();

        // Act
        metrics.RecordProduceCompleted(1.5, "kafka", "success");

        // Assert — duration histogram
        Assert.Single(capturedDoubles);
        Assert.Equal("emit.pipeline.produce.duration", capturedDoubles[0].Name);
        Assert.Equal(1.5, capturedDoubles[0].Value);
        Assert.Contains(capturedDoubles[0].Tags, t => t.Key == "provider" && t.Value?.ToString() == "kafka");
        Assert.Contains(capturedDoubles[0].Tags, t => t.Key == "result" && t.Value?.ToString() == "success");

        // Assert — completion counter
        Assert.Single(capturedLongs);
        Assert.Equal("emit.pipeline.produce.completed", capturedLongs[0].Name);
        Assert.Equal(1L, capturedLongs[0].Value);

        listener.Dispose();
    }

    [Fact]
    public void GivenEnrichmentTags_WhenRecording_ThenEnrichmentTagsAreAppended()
    {
        // Arrange
        var enrichmentTags = new KeyValuePair<string, object?>[]
        {
            new("environment", "production"),
            new("service", "emit-test")
        };
        var enrichment = new EmitMetricsEnrichment(enrichmentTags);
        var metrics = new EmitMetrics(null, enrichment);
        var listener = new MeterListener();
        var captured = new List<(string Name, object Value, KeyValuePair<string, object?>[] Tags)>();

        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterNames.Emit)
                listener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
            captured.Add((instrument.Name, value, tags.ToArray())));

        listener.Start();

        // Act
        metrics.RecordProduceCompleted(0.5, "kafka", "success");

        // Assert
        Assert.Single(captured);
        Assert.Contains(captured[0].Tags, t => t.Key == "environment" && t.Value?.ToString() == "production");
        Assert.Contains(captured[0].Tags, t => t.Key == "service" && t.Value?.ToString() == "emit-test");
        Assert.Contains(captured[0].Tags, t => t.Key == "provider" && t.Value?.ToString() == "kafka");
        Assert.Contains(captured[0].Tags, t => t.Key == "result" && t.Value?.ToString() == "success");

        listener.Dispose();
    }

    [Fact]
    public void GivenConsumeCompleted_WhenRecording_ThenRecordsDurationAndCounterWithCorrectTags()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var metrics = new EmitMetrics(null, enrichment);
        var listener = new MeterListener();
        var capturedDoubles = new List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)>();
        var capturedLongs = new List<(string Name, long Value, KeyValuePair<string, object?>[] Tags)>();

        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterNames.Emit)
                listener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
            capturedDoubles.Add((instrument.Name, value, tags.ToArray())));
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            capturedLongs.Add((instrument.Name, value, tags.ToArray())));

        listener.Start();

        // Act
        metrics.RecordConsumeCompleted(2.3, "kafka", "success", "TestConsumer");

        // Assert — duration histogram
        Assert.Single(capturedDoubles);
        Assert.Equal("emit.pipeline.consume.duration", capturedDoubles[0].Name);
        Assert.Equal(2.3, capturedDoubles[0].Value);
        Assert.Contains(capturedDoubles[0].Tags, t => t.Key == "provider" && t.Value?.ToString() == "kafka");
        Assert.Contains(capturedDoubles[0].Tags, t => t.Key == "result" && t.Value?.ToString() == "success");

        // Assert — completion counter
        Assert.Single(capturedLongs);
        Assert.Equal("emit.pipeline.consume.completed", capturedLongs[0].Name);
        Assert.Equal(1L, capturedLongs[0].Value);

        listener.Dispose();
    }

    [Fact]
    public void GivenErrorAction_WhenRecording_ThenMeasurementHasCorrectInstrumentNameAndTags()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var metrics = new EmitMetrics(null, enrichment);
        var listener = new MeterListener();
        var captured = new List<(string Name, object Value, KeyValuePair<string, object?>[] Tags)>();

        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterNames.Emit)
                listener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            captured.Add((instrument.Name, value, tags.ToArray())));

        listener.Start();

        // Act
        metrics.RecordErrorAction("retry");

        // Assert
        Assert.Single(captured);
        Assert.Equal("emit.consumer.error.actions", captured[0].Name);
        Assert.Equal(1L, captured[0].Value);
        Assert.Contains(captured[0].Tags, t => t.Key == "action" && t.Value?.ToString() == "retry");

        listener.Dispose();
    }

    [Fact]
    public void GivenRetryAttempts_WhenRecording_ThenMeasurementHasCorrectInstrumentNameAndTags()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var metrics = new EmitMetrics(null, enrichment);
        var listener = new MeterListener();
        var captured = new List<(string Name, object Value, KeyValuePair<string, object?>[] Tags)>();

        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterNames.Emit)
                listener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<int>((instrument, value, tags, state) =>
            captured.Add((instrument.Name, value, tags.ToArray())));

        listener.Start();

        // Act
        metrics.RecordRetryAttempts(3, "exhausted");

        // Assert
        Assert.Single(captured);
        Assert.Equal("emit.consumer.retry.attempts", captured[0].Name);
        Assert.Equal(3, captured[0].Value);
        Assert.Contains(captured[0].Tags, t => t.Key == "result" && t.Value?.ToString() == "exhausted");

        listener.Dispose();
    }

    [Fact]
    public void GivenDiscard_WhenRecording_ThenMeasurementHasCorrectInstrumentName()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var metrics = new EmitMetrics(null, enrichment);
        var listener = new MeterListener();
        var captured = new List<(string Name, object Value, KeyValuePair<string, object?>[] Tags)>();

        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterNames.Emit)
                listener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            captured.Add((instrument.Name, value, tags.ToArray())));

        listener.Start();

        // Act
        metrics.RecordDiscard();

        // Assert
        Assert.Single(captured);
        Assert.Equal("emit.consumer.discards", captured[0].Name);
        Assert.Equal(1L, captured[0].Value);

        listener.Dispose();
    }

    [Fact]
    public void GivenValidationCompleted_WhenRecording_ThenMeasurementHasCorrectInstrumentNameAndTags()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var metrics = new EmitMetrics(null, enrichment);
        var listener = new MeterListener();
        var captured = new List<(string Name, object Value, KeyValuePair<string, object?>[] Tags)>();

        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterNames.Emit)
                listener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            captured.Add((instrument.Name, value, tags.ToArray())));

        listener.Start();

        // Act
        metrics.RecordValidationCompleted("success", "continue");

        // Assert
        Assert.Single(captured);
        Assert.Equal("emit.consumer.validation.completed", captured[0].Name);
        Assert.Equal(1L, captured[0].Value);
        Assert.Contains(captured[0].Tags, t => t.Key == "result" && t.Value?.ToString() == "success");
        Assert.Contains(captured[0].Tags, t => t.Key == "action" && t.Value?.ToString() == "continue");

        listener.Dispose();
    }

    [Fact]
    public void GivenStateTransition_WhenRecording_ThenMeasurementIsTaggedWithStateGroupAndTopic()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var metrics = new EmitMetrics(null, enrichment);
        var listener = new MeterListener();
        var captured = new List<(string Name, object Value, KeyValuePair<string, object?>[] Tags)>();

        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterNames.Emit)
                listener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            captured.Add((instrument.Name, value, tags.ToArray())));

        listener.Start();

        // Act
        metrics.RecordStateTransition("group-1", "orders", "open");

        // Assert
        Assert.Single(captured);
        Assert.Equal("emit.consumer.circuit_breaker.state_transitions", captured[0].Name);
        Assert.Equal(1L, captured[0].Value);
        Assert.Contains(captured[0].Tags, t => t.Key == "to_state" && t.Value?.ToString() == "open");
        Assert.Contains(captured[0].Tags, t => t.Key == "consumer_group" && Equals(t.Value, "group-1"));
        Assert.Contains(captured[0].Tags, t => t.Key == "topic" && Equals(t.Value, "orders"));

        listener.Dispose();
    }

    [Fact]
    public void GivenTwoCircuitBreakersOnTheSameGroupId_WhenObserved_ThenEachReportsItsOwnStateTaggedWithItsTopic()
    {
        // Arrange: the same group id consuming two topics is two consumer group declarations, each with a breaker
        var metrics = new EmitMetrics(null, new EmitMetricsEnrichment());
        var groupId = $"group-{Guid.NewGuid():N}";
        metrics.RegisterCircuitBreaker($"orders-{groupId}", groupId, "orders", () => 1);
        metrics.RegisterCircuitBreaker($"payments-{groupId}", groupId, "payments", () => 0);

        // Act
        var states = ObserveCircuitBreakerStates(groupId);

        // Assert
        Assert.Equal(2, states.Count);
        Assert.Equal(1, states["orders"]);
        Assert.Equal(0, states["payments"]);
    }

    [Fact]
    public void GivenCircuitBreakerDeregistered_WhenObserved_ThenOnlyTheOtherIsReported()
    {
        // Arrange
        var metrics = new EmitMetrics(null, new EmitMetricsEnrichment());
        var groupId = $"group-{Guid.NewGuid():N}";
        metrics.RegisterCircuitBreaker($"orders-{groupId}", groupId, "orders", () => 1);
        metrics.RegisterCircuitBreaker($"payments-{groupId}", groupId, "payments", () => 2);

        // Act
        metrics.DeregisterCircuitBreaker($"orders-{groupId}");
        var states = ObserveCircuitBreakerStates(groupId);

        // Assert
        Assert.Equal(2, Assert.Single(states, s => s.Key == "payments").Value);
        Assert.Single(states);
    }

    [Fact]
    public void GivenOpenDuration_WhenRecording_ThenMeasurementIsTaggedWithGroupAndTopic()
    {
        // Arrange
        var metrics = new EmitMetrics(null, new EmitMetricsEnrichment());
        using var listener = new MeterListener();
        var captured = new List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)>();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == MeterNames.Emit)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            captured.Add((instrument.Name, value, tags.ToArray())));
        listener.Start();

        // Act
        metrics.RecordCircuitBreakerOpenDuration("group-1", "orders", 5);

        // Assert
        var measurement = Assert.Single(captured);
        Assert.Equal("emit.consumer.circuit_breaker.open_duration", measurement.Name);
        Assert.Equal(5, measurement.Value);
        Assert.Contains(measurement.Tags, t => t.Key == "consumer_group" && Equals(t.Value, "group-1"));
        Assert.Contains(measurement.Tags, t => t.Key == "topic" && Equals(t.Value, "orders"));
    }

    /// <summary>
    /// Reads the circuit breaker state gauge, by topic, for the breakers of one group id. Gauges of every
    /// <see cref="EmitMetrics"/> in the process share the meter name, so the group id keeps other tests out.
    /// </summary>
    private static Dictionary<string, int> ObserveCircuitBreakerStates(string groupId)
    {
        var states = new Dictionary<string, int>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == MeterNames.Emit && instrument.Name == "emit.consumer.circuit_breaker.state")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<int>((_, value, tags, _) =>
        {
            var all = tags.ToArray();
            if (all.Any(t => t.Key == "consumer_group" && Equals(t.Value, groupId)))
                states.Add((string)all.Single(t => t.Key == "topic").Value!, value);
        });
        listener.Start();
        listener.RecordObservableInstruments();
        return states;
    }

    [Fact]
    public void GivenDlqProduced_WhenRecording_ThenMeasurementHasCorrectInstrumentNameAndTags()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var metrics = new EmitMetrics(null, enrichment);
        var listener = new MeterListener();
        var captured = new List<(string Name, object Value, KeyValuePair<string, object?>[] Tags)>();

        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterNames.Emit)
                listener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            captured.Add((instrument.Name, value, tags.ToArray())));

        listener.Start();

        // Act
        metrics.RecordDlqProduced("validation_failure");

        // Assert
        Assert.Single(captured);
        Assert.Equal("emit.consumer.dlq.produced", captured[0].Name);
        Assert.Equal(1L, captured[0].Value);
        Assert.Contains(captured[0].Tags, t => t.Key == "reason" && t.Value?.ToString() == "validation_failure");

        listener.Dispose();
    }

    [Fact]
    public void GivenDlqProduceErrors_WhenRecording_ThenMeasurementHasCorrectInstrumentNameAndTags()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var metrics = new EmitMetrics(null, enrichment);
        var listener = new MeterListener();
        var captured = new List<(string Name, object Value, KeyValuePair<string, object?>[] Tags)>();

        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterNames.Emit)
                listener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            captured.Add((instrument.Name, value, tags.ToArray())));

        listener.Start();

        // Act
        metrics.RecordDlqProduceErrors("timeout");

        // Assert
        Assert.Single(captured);
        Assert.Equal("emit.consumer.dlq.produce_errors", captured[0].Name);
        Assert.Equal(1L, captured[0].Value);
        Assert.Contains(captured[0].Tags, t => t.Key == "reason" && t.Value?.ToString() == "timeout");

        listener.Dispose();
    }
}

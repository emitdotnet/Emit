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
    public void GivenProduceDuration_WhenRecording_ThenMeasurementHasCorrectInstrumentNameAndTags()
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

        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
            captured.Add((instrument.Name, value, tags.ToArray())));

        listener.Start();

        // Act
        metrics.RecordProduceDuration(1.5, "kafka", "success");

        // Assert
        Assert.Single(captured);
        Assert.Equal("emit.pipeline.produce.duration", captured[0].Name);
        Assert.Equal(1.5, captured[0].Value);
        Assert.Contains(captured[0].Tags, t => t.Key == "provider" && t.Value?.ToString() == "kafka");
        Assert.Contains(captured[0].Tags, t => t.Key == "result" && t.Value?.ToString() == "success");

        listener.Dispose();
    }

    [Fact]
    public void GivenProduceCompleted_WhenRecording_ThenMeasurementHasCorrectInstrumentNameAndTags()
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
        metrics.RecordProduceCompleted("kafka", "error");

        // Assert
        Assert.Single(captured);
        Assert.Equal("emit.pipeline.produce.completed", captured[0].Name);
        Assert.Equal(1L, captured[0].Value);
        Assert.Contains(captured[0].Tags, t => t.Key == "provider" && t.Value?.ToString() == "kafka");
        Assert.Contains(captured[0].Tags, t => t.Key == "result" && t.Value?.ToString() == "error");

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
        metrics.RecordProduceDuration(0.5, "kafka", "success");

        // Assert
        Assert.Single(captured);
        Assert.Contains(captured[0].Tags, t => t.Key == "environment" && t.Value?.ToString() == "production");
        Assert.Contains(captured[0].Tags, t => t.Key == "service" && t.Value?.ToString() == "emit-test");
        Assert.Contains(captured[0].Tags, t => t.Key == "provider" && t.Value?.ToString() == "kafka");
        Assert.Contains(captured[0].Tags, t => t.Key == "result" && t.Value?.ToString() == "success");

        listener.Dispose();
    }

    [Fact]
    public void GivenConsumeDuration_WhenRecording_ThenMeasurementHasCorrectInstrumentNameAndTags()
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

        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
            captured.Add((instrument.Name, value, tags.ToArray())));

        listener.Start();

        // Act
        metrics.RecordConsumeDuration(2.3, "kafka", "success", "TestConsumer");

        // Assert
        Assert.Single(captured);
        Assert.Equal("emit.pipeline.consume.duration", captured[0].Name);
        Assert.Equal(2.3, captured[0].Value);
        Assert.Contains(captured[0].Tags, t => t.Key == "provider" && t.Value?.ToString() == "kafka");
        Assert.Contains(captured[0].Tags, t => t.Key == "result" && t.Value?.ToString() == "success");

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
    public void GivenStateTransition_WhenRecording_ThenMeasurementHasCorrectInstrumentNameAndTags()
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
        metrics.RecordStateTransition("open");

        // Assert
        Assert.Single(captured);
        Assert.Equal("emit.consumer.circuit_breaker.state_transitions", captured[0].Name);
        Assert.Equal(1L, captured[0].Value);
        Assert.Contains(captured[0].Tags, t => t.Key == "to_state" && t.Value?.ToString() == "open");

        listener.Dispose();
    }

    [Fact]
    public void GivenCircuitBreakerStateCallback_WhenRegistered_ThenGaugeReportsCallbackValue()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var callbackInvoked = false;
        var capturedValue = 0;
        var metrics = new EmitMetrics(null, enrichment);

        // Act
        metrics.RegisterCircuitBreakerStateCallback(() =>
        {
            callbackInvoked = true;
            return 1; // Open state
        });

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterNames.Emit && instrument.Name == "emit.consumer.circuit_breaker.state")
                listener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<int>((instrument, value, tags, state) =>
        {
            if (instrument.Name == "emit.consumer.circuit_breaker.state")
                capturedValue = value;
        });

        listener.Start();
        listener.RecordObservableInstruments();

        // Assert
        Assert.True(callbackInvoked);
        Assert.Equal(1, capturedValue);
    }

    [Fact]
    public void GivenNoCircuitBreakerStateCallback_WhenObserved_ThenGaugeReportsZero()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var capturedValue = -1;
        var metrics = new EmitMetrics(null, enrichment);

        // Act
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterNames.Emit && instrument.Name == "emit.consumer.circuit_breaker.state")
                listener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<int>((instrument, value, tags, state) =>
        {
            if (instrument.Name == "emit.consumer.circuit_breaker.state")
                capturedValue = value;
        });

        listener.Start();
        listener.RecordObservableInstruments();

        // Assert
        Assert.Equal(0, capturedValue);
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
        metrics.RecordDlqProduced("validation_failure", "my-topic.dlq");

        // Assert
        Assert.Single(captured);
        Assert.Equal("emit.consumer.dlq.produced", captured[0].Name);
        Assert.Equal(1L, captured[0].Value);
        Assert.Contains(captured[0].Tags, t => t.Key == "reason" && t.Value?.ToString() == "validation_failure");
        Assert.Contains(captured[0].Tags, t => t.Key == "dlq_topic" && t.Value?.ToString() == "my-topic.dlq");

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
        metrics.RecordDlqProduceErrors("timeout", "my-topic.dlq");

        // Assert
        Assert.Single(captured);
        Assert.Equal("emit.consumer.dlq.produce_errors", captured[0].Name);
        Assert.Equal(1L, captured[0].Value);
        Assert.Contains(captured[0].Tags, t => t.Key == "reason" && t.Value?.ToString() == "timeout");
        Assert.Contains(captured[0].Tags, t => t.Key == "dlq_topic" && t.Value?.ToString() == "my-topic.dlq");

        listener.Dispose();
    }
}

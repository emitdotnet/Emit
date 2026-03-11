namespace Emit.Tests.Metrics;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using global::Emit.Abstractions;
using global::Emit.Abstractions.Metrics;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Metrics;
using Xunit;

[Collection("MetricsTests")]
public sealed class ProduceMetricsMiddlewareTests
{
    [Fact]
    public async Task GivenSuccessfulExecution_WhenInvoking_ThenRecordsDurationAndCompletedWithSuccess()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var metrics = new EmitMetrics(null, enrichment);
        var middleware = new ProduceMetricsMiddleware<string>(metrics);
        var listener = new MeterListener();
        var captured = new List<(string Name, object Value, KeyValuePair<string, object?>[] Tags)>();

        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterNames.Emit)
                listener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
            captured.Add((instrument.Name, value, tags.ToArray())));

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            captured.Add((instrument.Name, value, tags.ToArray())));

        listener.Start();

        var context = new TestSendContext<string>
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = new TestServiceProvider(),
            Message = "test-message"
        };

        context.DestinationAddress = EmitEndpointAddress.ForEntity("kafka", "broker", 9092, "test-topic");

        var nextInvoked = false;
        IMiddlewarePipeline<SendContext<string>> next = new TestPipeline<SendContext<string>>(ctx =>
        {
            nextInvoked = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.True(nextInvoked);
        Assert.Equal(2, captured.Count);

        var duration = captured.First(c => c.Name == "emit.pipeline.produce.duration");
        Assert.True((double)duration.Value > 0);
        Assert.Contains(duration.Tags, t => t.Key == "provider" && t.Value?.ToString() == "kafka");
        Assert.Contains(duration.Tags, t => t.Key == "result" && t.Value?.ToString() == "success");

        var completed = captured.First(c => c.Name == "emit.pipeline.produce.completed");
        Assert.Equal(1L, completed.Value);
        Assert.Contains(completed.Tags, t => t.Key == "provider" && t.Value?.ToString() == "kafka");
        Assert.Contains(completed.Tags, t => t.Key == "result" && t.Value?.ToString() == "success");

        listener.Dispose();
    }

    [Fact]
    public async Task GivenFailedExecution_WhenInvoking_ThenRecordsDurationAndCompletedWithErrorAndRethrows()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var metrics = new EmitMetrics(null, enrichment);
        var middleware = new ProduceMetricsMiddleware<string>(metrics);
        var listener = new MeterListener();
        var captured = new List<(string Name, object Value, KeyValuePair<string, object?>[] Tags)>();

        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterNames.Emit)
                listener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
            captured.Add((instrument.Name, value, tags.ToArray())));

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            captured.Add((instrument.Name, value, tags.ToArray())));

        listener.Start();

        var context = new TestSendContext<string>
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = new TestServiceProvider(),
            Message = "test-message"
        };

        context.DestinationAddress = EmitEndpointAddress.ForEntity("kafka", "broker", 9092, "test-topic");

        var expectedException = new InvalidOperationException("Test failure");
        IMiddlewarePipeline<SendContext<string>> next = new TestPipeline<SendContext<string>>(_ => throw expectedException);

        // Act & Assert
        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context, next));
        Assert.Same(expectedException, actualException);

        Assert.Equal(2, captured.Count);

        var duration = captured.First(c => c.Name == "emit.pipeline.produce.duration");
        Assert.True((double)duration.Value > 0);
        Assert.Contains(duration.Tags, t => t.Key == "provider" && t.Value?.ToString() == "kafka");
        Assert.Contains(duration.Tags, t => t.Key == "result" && t.Value?.ToString() == "error");

        var completed = captured.First(c => c.Name == "emit.pipeline.produce.completed");
        Assert.Equal(1L, completed.Value);
        Assert.Contains(completed.Tags, t => t.Key == "provider" && t.Value?.ToString() == "kafka");
        Assert.Contains(completed.Tags, t => t.Key == "result" && t.Value?.ToString() == "error");

        listener.Dispose();
    }

    [Fact]
    public async Task GivenEnrichmentTags_WhenInvoking_ThenEnrichmentTagsFlowThrough()
    {
        // Arrange
        var enrichmentTags = new KeyValuePair<string, object?>[]
        {
            new("environment", "test"),
            new("service", "emit")
        };
        var enrichment = new EmitMetricsEnrichment(enrichmentTags);
        var metrics = new EmitMetrics(null, enrichment);
        var middleware = new ProduceMetricsMiddleware<string>(metrics);
        var listener = new MeterListener();
        var captured = new List<(string Name, object Value, KeyValuePair<string, object?>[] Tags)>();

        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterNames.Emit)
                listener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
            captured.Add((instrument.Name, value, tags.ToArray())));

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            captured.Add((instrument.Name, value, tags.ToArray())));

        listener.Start();

        var context = new TestSendContext<string>
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = new TestServiceProvider(),
            Message = "test-message"
        };

        context.DestinationAddress = EmitEndpointAddress.ForEntity("kafka", "broker", 9092, "test-topic");

        IMiddlewarePipeline<SendContext<string>> next = new TestPipeline<SendContext<string>>(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.Equal(2, captured.Count);

        foreach (var measurement in captured)
        {
            Assert.Contains(measurement.Tags, t => t.Key == "environment" && t.Value?.ToString() == "test");
            Assert.Contains(measurement.Tags, t => t.Key == "service" && t.Value?.ToString() == "emit");
            Assert.Contains(measurement.Tags, t => t.Key == "provider" && t.Value?.ToString() == "kafka");
            Assert.Contains(measurement.Tags, t => t.Key == "result" && t.Value?.ToString() == "success");
        }

        listener.Dispose();
    }

    [Fact]
    public async Task GivenNoProviderIdentifierFeature_WhenInvoking_ThenUsesUnknownAsProvider()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var metrics = new EmitMetrics(null, enrichment);
        var middleware = new ProduceMetricsMiddleware<string>(metrics);
        var listener = new MeterListener();
        var captured = new List<(string Name, object Value, KeyValuePair<string, object?>[] Tags)>();

        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterNames.Emit)
                listener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
            captured.Add((instrument.Name, value, tags.ToArray())));

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            captured.Add((instrument.Name, value, tags.ToArray())));

        listener.Start();

        var context = new TestSendContext<string>
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = new TestServiceProvider(),
            Message = "test-message"
        };

        IMiddlewarePipeline<SendContext<string>> next = new TestPipeline<SendContext<string>>(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.Equal(2, captured.Count);

        foreach (var measurement in captured)
        {
            Assert.Contains(measurement.Tags, t => t.Key == "provider" && t.Value?.ToString() == "unknown");
        }

        listener.Dispose();
    }

    private sealed class TestSendContext<T> : SendContext<T>;

    private sealed class TestServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}

namespace Emit.Tests.Metrics;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using global::Emit.Abstractions;
using global::Emit.Abstractions.Metrics;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Metrics;
using Xunit;

[Collection("MetricsTests")]
public sealed class ConsumeMetricsMiddlewareTests
{
    [Fact]
    public async Task GivenSuccessfulExecution_WhenInvoking_ThenRecordsDurationAndCompletedWithSuccess()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var metrics = new EmitMetrics(null, enrichment);
        var middleware = new ConsumeMetricsMiddleware<string>(metrics);
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

        var context = new TestInboundContext<string>
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = new TestServiceProvider(),
            Message = "test-message"
        };

        context.Features.Set<IProviderIdentifierFeature>(new TestProviderIdentifierFeature { ProviderId = "kafka" });

        var nextInvoked = false;
        Task Next(InboundContext<string> ctx)
        {
            nextInvoked = true;
            return Task.CompletedTask;
        }

        // Act
        await middleware.InvokeAsync(context, Next);

        // Assert
        Assert.True(nextInvoked);
        Assert.Equal(2, captured.Count);

        var duration = captured.First(c => c.Name == "emit.pipeline.consume.duration");
        Assert.True((double)duration.Value > 0);
        Assert.Contains(duration.Tags, t => t.Key == "provider" && t.Value?.ToString() == "kafka");
        Assert.Contains(duration.Tags, t => t.Key == "result" && t.Value?.ToString() == "success");

        var completed = captured.First(c => c.Name == "emit.pipeline.consume.completed");
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
        var middleware = new ConsumeMetricsMiddleware<string>(metrics);
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

        var context = new TestInboundContext<string>
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = new TestServiceProvider(),
            Message = "test-message"
        };

        context.Features.Set<IProviderIdentifierFeature>(new TestProviderIdentifierFeature { ProviderId = "kafka" });

        var expectedException = new InvalidOperationException("Test failure");
        Task Next(InboundContext<string> ctx) => throw expectedException;

        // Act & Assert
        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context, Next));
        Assert.Same(expectedException, actualException);

        Assert.Equal(2, captured.Count);

        var duration = captured.First(c => c.Name == "emit.pipeline.consume.duration");
        Assert.True((double)duration.Value > 0);
        Assert.Contains(duration.Tags, t => t.Key == "provider" && t.Value?.ToString() == "kafka");
        Assert.Contains(duration.Tags, t => t.Key == "result" && t.Value?.ToString() == "error");

        var completed = captured.First(c => c.Name == "emit.pipeline.consume.completed");
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
        var middleware = new ConsumeMetricsMiddleware<string>(metrics);
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

        var context = new TestInboundContext<string>
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = new TestServiceProvider(),
            Message = "test-message"
        };

        context.Features.Set<IProviderIdentifierFeature>(new TestProviderIdentifierFeature { ProviderId = "kafka" });

        Task Next(InboundContext<string> ctx) => Task.CompletedTask;

        // Act
        await middleware.InvokeAsync(context, Next);

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
        var middleware = new ConsumeMetricsMiddleware<string>(metrics);
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

        var context = new TestInboundContext<string>
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = new TestServiceProvider(),
            Message = "test-message"
        };

        Task Next(InboundContext<string> ctx) => Task.CompletedTask;

        // Act
        await middleware.InvokeAsync(context, Next);

        // Assert
        Assert.Equal(2, captured.Count);

        foreach (var measurement in captured)
        {
            Assert.Contains(measurement.Tags, t => t.Key == "provider" && t.Value?.ToString() == "unknown");
        }

        listener.Dispose();
    }

    private sealed class TestInboundContext<T> : InboundContext<T>;

    private sealed class TestProviderIdentifierFeature : IProviderIdentifierFeature
    {
        public required string ProviderId { get; init; }
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}

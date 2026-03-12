namespace Emit.Mediator.Tests.Metrics;

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using global::Emit.Abstractions;
using global::Emit.Abstractions.Metrics;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Mediator;
using global::Emit.Mediator.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Collection("MetricsTests")]
public sealed class MediatorMetricsMiddlewareTests : IDisposable
{
    private readonly MeterListener listener = new();
    private readonly List<(double value, KeyValuePair<string, object?>[] tags)> histogramMeasurements = [];
    private readonly List<(long value, KeyValuePair<string, object?>[] tags)> counterMeasurements = [];
    private readonly List<(long value, KeyValuePair<string, object?>[] tags)> upDownCounterMeasurements = [];

    public MediatorMetricsMiddlewareTests()
    {
        listener.InstrumentPublished = (instrument, listenerInstance) =>
        {
            if (instrument.Meter.Name == MeterNames.EmitMediator)
            {
                listenerInstance.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
        {
            histogramMeasurements.Add((measurement, tags.ToArray()));
        });

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name == "emit.mediator.send.active")
            {
                upDownCounterMeasurements.Add((measurement, tags.ToArray()));
            }
            else
            {
                counterMeasurements.Add((measurement, tags.ToArray()));
            }
        });

        listener.Start();
    }

    public void Dispose()
    {
        listener.Dispose();
    }

    [Fact]
    public async Task GivenSuccessfulInvocation_WhenInvokeAsync_ThenRecordsDurationAndCompletedWithSuccessResult()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var mediatorMetrics = new MediatorMetrics(null, enrichment);
        var middleware = new MediatorMetricsMiddleware<TestRequest>(mediatorMetrics);
        var context = new TestMediatorContext(new TestRequest("test"));
        var invoked = false;

        IMiddlewarePipeline<MediatorContext<TestRequest>> next = new TestPipeline<MediatorContext<TestRequest>>(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.True(invoked);

        var duration = Assert.Single(histogramMeasurements);
        Assert.True(duration.value > 0);
        Assert.Equal(2, duration.tags.Length);
        Assert.Contains(duration.tags, tag => tag.Key == "request_type" && Equals(tag.Value, nameof(TestRequest)));
        Assert.Contains(duration.tags, tag => tag.Key == "result" && Equals(tag.Value, "success"));

        var completed = Assert.Single(counterMeasurements);
        Assert.Equal(1, completed.value);
        Assert.Equal(2, completed.tags.Length);
        Assert.Contains(completed.tags, tag => tag.Key == "request_type" && Equals(tag.Value, nameof(TestRequest)));
        Assert.Contains(completed.tags, tag => tag.Key == "result" && Equals(tag.Value, "success"));
    }

    [Fact]
    public async Task GivenFailedInvocation_WhenInvokeAsync_ThenRecordsDurationAndCompletedWithErrorResultAndRethrows()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var mediatorMetrics = new MediatorMetrics(null, enrichment);
        var middleware = new MediatorMetricsMiddleware<TestRequest>(mediatorMetrics);
        var context = new TestMediatorContext(new TestRequest("test"));
        var expectedException = new InvalidOperationException("Handler failed");

        IMiddlewarePipeline<MediatorContext<TestRequest>> next = new TestPipeline<MediatorContext<TestRequest>>(_ => throw expectedException);

        // Act & Assert
        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(context, next));
        Assert.Same(expectedException, actualException);

        var duration = Assert.Single(histogramMeasurements);
        Assert.True(duration.value > 0);
        Assert.Equal(2, duration.tags.Length);
        Assert.Contains(duration.tags, tag => tag.Key == "request_type" && Equals(tag.Value, nameof(TestRequest)));
        Assert.Contains(duration.tags, tag => tag.Key == "result" && Equals(tag.Value, "error"));

        var completed = Assert.Single(counterMeasurements);
        Assert.Equal(1, completed.value);
        Assert.Equal(2, completed.tags.Length);
        Assert.Contains(completed.tags, tag => tag.Key == "request_type" && Equals(tag.Value, nameof(TestRequest)));
        Assert.Contains(completed.tags, tag => tag.Key == "result" && Equals(tag.Value, "error"));
    }

    [Fact]
    public async Task GivenSuccessfulInvocation_WhenInvokeAsync_ThenIncrementsAndDecrementsActiveCounter()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var mediatorMetrics = new MediatorMetrics(null, enrichment);
        var middleware = new MediatorMetricsMiddleware<TestRequest>(mediatorMetrics);
        var context = new TestMediatorContext(new TestRequest("test"));

        IMiddlewarePipeline<MediatorContext<TestRequest>> next = new TestPipeline<MediatorContext<TestRequest>>(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.Equal(2, upDownCounterMeasurements.Count);

        var increment = upDownCounterMeasurements[0];
        Assert.Equal(1, increment.value);

        var decrement = upDownCounterMeasurements[1];
        Assert.Equal(-1, decrement.value);
    }

    [Fact]
    public async Task GivenFailedInvocation_WhenInvokeAsync_ThenDecrementsActiveCounterInFinally()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var mediatorMetrics = new MediatorMetrics(null, enrichment);
        var middleware = new MediatorMetricsMiddleware<TestRequest>(mediatorMetrics);
        var context = new TestMediatorContext(new TestRequest("test"));

        IMiddlewarePipeline<MediatorContext<TestRequest>> next = new TestPipeline<MediatorContext<TestRequest>>(_ => throw new InvalidOperationException());

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(context, next));

        // Assert
        Assert.Equal(2, upDownCounterMeasurements.Count);

        var increment = upDownCounterMeasurements[0];
        Assert.Equal(1, increment.value);

        var decrement = upDownCounterMeasurements[1];
        Assert.Equal(-1, decrement.value);
    }

    [Fact]
    public async Task GivenEnrichmentTags_WhenInvokeAsync_ThenAppendsEnrichmentTagsToAllMeasurements()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment(new KeyValuePair<string, object?>[]
        {
            new("env", "production"),
            new("region", "us-west")
        });
        var mediatorMetrics = new MediatorMetrics(null, enrichment);
        var middleware = new MediatorMetricsMiddleware<TestRequest>(mediatorMetrics);
        var context = new TestMediatorContext(new TestRequest("test"));

        IMiddlewarePipeline<MediatorContext<TestRequest>> next = new TestPipeline<MediatorContext<TestRequest>>(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        var duration = Assert.Single(histogramMeasurements);
        Assert.Equal(4, duration.tags.Length);
        Assert.Contains(duration.tags, tag => tag.Key == "env" && Equals(tag.Value, "production"));
        Assert.Contains(duration.tags, tag => tag.Key == "region" && Equals(tag.Value, "us-west"));
        Assert.Contains(duration.tags, tag => tag.Key == "request_type" && Equals(tag.Value, nameof(TestRequest)));
        Assert.Contains(duration.tags, tag => tag.Key == "result" && Equals(tag.Value, "success"));

        var completed = Assert.Single(counterMeasurements);
        Assert.Equal(4, completed.tags.Length);
        Assert.Contains(completed.tags, tag => tag.Key == "env" && Equals(tag.Value, "production"));
        Assert.Contains(completed.tags, tag => tag.Key == "region" && Equals(tag.Value, "us-west"));
    }

    [Fact]
    public async Task GivenDifferentRequestTypes_WhenInvokeAsync_ThenRecordsCorrectRequestTypeTag()
    {
        // Arrange
        var enrichment = new EmitMetricsEnrichment();
        var mediatorMetrics = new MediatorMetrics(null, enrichment);
        var middleware1 = new MediatorMetricsMiddleware<TestRequest>(mediatorMetrics);
        var middleware2 = new MediatorMetricsMiddleware<AnotherRequest>(mediatorMetrics);
        var context1 = new TestMediatorContext(new TestRequest("test"));
        var context2 = new AnotherMediatorContext(new AnotherRequest());

        IMiddlewarePipeline<MediatorContext<TestRequest>> next1 = new TestPipeline<MediatorContext<TestRequest>>(_ => Task.CompletedTask);
        IMiddlewarePipeline<MediatorContext<AnotherRequest>> next2 = new TestPipeline<MediatorContext<AnotherRequest>>(_ => Task.CompletedTask);

        // Act
        await middleware1.InvokeAsync(context1, next1);
        await middleware2.InvokeAsync(context2, next2);

        // Assert
        Assert.Equal(2, histogramMeasurements.Count);

        var testRequestMeasurement = histogramMeasurements.First(m =>
            m.tags.Any(t => t.Key == "request_type" && Equals(t.Value, nameof(TestRequest))));
        Assert.True(testRequestMeasurement.value > 0);

        var anotherRequestMeasurement = histogramMeasurements.First(m =>
            m.tags.Any(t => t.Key == "request_type" && Equals(t.Value, nameof(AnotherRequest))));
        Assert.True(anotherRequestMeasurement.value > 0);
    }

    private sealed record TestRequest(string Value) : IRequest;

    private sealed record AnotherRequest : IRequest;

    private sealed class TestMediatorContext : MediatorContext<TestRequest>
    {
        [SetsRequiredMembers]
        public TestMediatorContext(TestRequest message)
        {
            Message = message;
            MessageId = Guid.NewGuid().ToString();
            Timestamp = DateTimeOffset.UtcNow;
            CancellationToken = CancellationToken.None;
            Services = new ServiceCollection().BuildServiceProvider();
        }
    }

    private sealed class AnotherMediatorContext : MediatorContext<AnotherRequest>
    {
        [SetsRequiredMembers]
        public AnotherMediatorContext(AnotherRequest message)
        {
            Message = message;
            MessageId = Guid.NewGuid().ToString();
            Timestamp = DateTimeOffset.UtcNow;
            CancellationToken = CancellationToken.None;
            Services = new ServiceCollection().BuildServiceProvider();
        }
    }
}

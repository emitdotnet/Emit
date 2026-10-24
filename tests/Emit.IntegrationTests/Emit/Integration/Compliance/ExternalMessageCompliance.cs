namespace Emit.IntegrationTests.Integration.Compliance;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Emit.Abstractions;
using Emit.Abstractions.Metrics;
using Emit.Abstractions.Tracing;
using Emit.DependencyInjection;
using Emit.Kafka.Consumer;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for consuming messages produced externally (without Emit headers).
/// Verifies that they are delivered and metrics are recorded. Derived classes
/// configure a provider-specific consumer group and implement native message production.
/// </summary>
[Trait("Category", "Integration")]
public abstract class ExternalMessageCompliance
{
    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a consumer
    /// group containing <see cref="SinkConsumer{TMessage}"/> of <see cref="string"/>.
    /// No producer registration is needed.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    protected abstract void ConfigureEmit(EmitBuilder emit, string topic);

    /// <summary>
    /// Produces a message directly via the provider's native client, bypassing the Emit pipeline.
    /// The message must not include any Emit headers (e.g., no traceparent).
    /// </summary>
    /// <param name="topic">The topic to produce to.</param>
    /// <param name="key">The message key.</param>
    /// <param name="value">The message value.</param>
    protected abstract Task ProduceExternalMessageAsync(string topic, string key, string value);

    [Fact]
    public async Task GivenExternalMessageWithoutHeaders_WhenConsumed_ThenDeliveredAndRecordsMetrics()
    {
        // Arrange
        var topic = $"test-external-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var capturedMetrics = new ConcurrentBag<(string Name, object Value, KeyValuePair<string, object?>[] Tags)>();

        // Set up MeterListener to capture metrics from the Emit meter.
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterNames.Emit)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            capturedMetrics.Add((instrument.Name, value, tags.ToArray())));
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            capturedMetrics.Add((instrument.Name, value, tags.ToArray())));
        meterListener.Start();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmit(emit, topic));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce externally via the provider's native client (no Emit pipeline, no headers)
            await ProduceExternalMessageAsync(topic, "external-key", "external-value");

            // Assert — message received correctly
            var context = await sink.WaitForMessageAsync();
            var kafkaContext = Assert.IsType<KafkaTransportContext<string>>(context.TransportContext);
            Assert.Equal("external-key", kafkaContext.Key);
            Assert.Equal("external-value", context.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }

        // Assert — metrics were recorded
        Assert.Contains(capturedMetrics, m =>
            m.Name == "emit.pipeline.consume.duration");
        Assert.Contains(capturedMetrics, m =>
            m.Name == "emit.pipeline.consume.completed"
            && m.Tags.Any(t => t.Key == "result" && (string?)t.Value == "success"));
    }
}

namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Kafka.Consumer;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for basic produce/consume message flow. Derived classes configure
/// a provider-specific producer and consumer group for a <c>string, string</c> topic.
/// The consumer group must include <see cref="SinkConsumer{TMessage}"/> of <see cref="string"/>.
/// </summary>
[Trait("Category", "Integration")]
public abstract class ProduceConsumeCompliance
{
    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer
    /// and a consumer group containing <see cref="SinkConsumer{TMessage}"/> of <see cref="string"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    protected abstract void ConfigureEmit(EmitBuilder emit, string topic);

    [Fact]
    public async Task GivenProducerAndConsumer_WhenMessageProduced_ThenConsumerReceivesIt()
    {
        // Arrange
        var topic = $"test-direct-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();

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
            // Act
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("my-key", "my-value"));

            // Assert
            var context = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            var kafkaContext = Assert.IsType<KafkaTransportContext<string>>(context.TransportContext);
            Assert.Equal("my-key", kafkaContext.Key);
            Assert.Equal("my-value", context.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}

namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Compliance tests for the [Transactional] attribute on routed consumer handlers.
/// Verifies that each routed handler decorated with [Transactional] receives its own
/// usable unit-of-work transaction, allowing atomic business writes + outbox enqueue.
/// </summary>
[Trait("Category", "Integration")]
public abstract class TransactionalRouterCompliance : IAsyncLifetime
{
    /// <summary>
    /// Gets the Kafka bootstrap servers address for producing and consuming messages.
    /// </summary>
    protected abstract string BootstrapServers { get; }

    /// <summary>
    /// Configures the persistence provider (MongoDB or EF Core) with outbox enabled.
    /// Kafka configuration is handled by the base class.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="pollingInterval">The outbox daemon polling interval.</param>
    protected abstract void ConfigurePersistence(EmitBuilder emit, TimeSpan pollingInterval);

    /// <inheritdoc />
    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GivenTransactionalRoutedConsumer_WhenFiveMessagesConsumed_ThenAllFiveDeliveredViaOutbox()
    {
        // Arrange
        var inputTopic = $"test-txn-router-in-{Guid.NewGuid():N}";
        var outputTopic = $"test-txn-router-out-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit =>
                {
                    ConfigurePersistence(emit, pollingInterval);
                    ConfigureKafka(emit, inputTopic, outputTopic, groupId);
                });
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce 5 messages routed to the [Transactional] handler.
            for (var i = 1; i <= 5; i++)
            {
                await ProduceDirectAsync(inputTopic, $"k{i}", $"produce:msg-{i}");
            }

            // Assert — all 5 messages should arrive via the outbox on the output topic.
            var received = new List<string>();
            for (var i = 0; i < 5; i++)
            {
                var ctx = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
                received.Add(ctx.Message!);
            }

            Assert.Equal(5, received.Count);
            for (var i = 1; i <= 5; i++)
            {
                Assert.Contains($"msg-{i}", received);
            }
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private void ConfigureKafka(
        EmitBuilder emit,
        string inputTopic,
        string outputTopic,
        string groupId)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config =>
            {
                config.BootstrapServers = BootstrapServers;
            });
            kafka.AutoProvision();

            kafka.Topic<string, string>(inputTopic, t =>
            {
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.OnError(error => error.Default(a => a.Discard()));

                    group.AddRouter<string>(
                        "txn-router",
                        ctx => ctx.Message!.Split(':')[0],
                        routes =>
                        {
                            routes.Route<TransactionalRoutedProducingConsumer>("produce");
                        });
                });
            });

            kafka.Topic<string, string>(outputTopic, t =>
            {
                t.SetUtf8KeySerializer();
                t.SetUtf8ValueSerializer();
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.Producer();
                t.ConsumerGroup($"{groupId}-out", group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<SinkConsumer<string>>();
                });
            });
        });
    }

    private async Task ProduceDirectAsync(string topic, string key, string value)
    {
        using var producer = new ConfluentKafka.ProducerBuilder<string, string>(
            new ConfluentKafka.ProducerConfig { BootstrapServers = BootstrapServers })
            .Build();

        await producer.ProduceAsync(
            topic,
            new ConfluentKafka.Message<string, string> { Key = key, Value = value });
    }
}

/// <summary>
/// A [Transactional] routed consumer that produces the consumed message value to the output
/// topic via the transactional outbox. The route key is the prefix before ':' in the message.
/// </summary>
[Transactional]
public sealed class TransactionalRoutedProducingConsumer(
    IEventProducer<string, string> producer) : IConsumer<string>
{
    /// <inheritdoc />
    public async Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
    {
        // Extract payload after the route key prefix "produce:"
        var payload = context.Message!.Split(':')[1];
        await producer.ProduceAsync(
            new EventMessage<string, string>("key", payload), cancellationToken);
    }
}

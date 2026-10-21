namespace Emit.IntegrationTests.Integration.Compliance;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.Tracing;
using Emit.Configuration;
using Emit.DependencyInjection;
using Emit.Kafka.Consumer;
using Emit.Kafka.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Compliance tests for tombstones: a message with a key and no value, produced through the outbox or directly,
/// arrives as a Kafka message with a null value, in order with its key's other messages. Derived classes configure
/// a persistence provider with the outbox enabled; Kafka is configured by the base class.
/// </summary>
[Trait("Category", "Integration")]
public abstract class TombstoneCompliance : IAsyncLifetime
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets the Kafka bootstrap servers address.
    /// </summary>
    protected abstract string BootstrapServers { get; }

    /// <summary>
    /// Gets the schema registry address.
    /// </summary>
    protected abstract string SchemaRegistryUrl { get; }

    /// <summary>
    /// Configures the persistence provider with the outbox enabled.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="configureOutbox">Configures the outbox the provider enables.</param>
    protected abstract void ConfigurePersistence(EmitBuilder emit, Action<OutboxOptions> configureOutbox);

    /// <summary>
    /// Hook for providers that must save tracked changes, called after each message produced. Does nothing by default.
    /// </summary>
    protected virtual Task FlushBeforeCommitAsync(IServiceProvider scopedServices) => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GivenValueThenTombstoneOfAKeyThroughTheOutbox_WhenDelivered_ThenTheTombstoneFollowsTheValueWithTheKeyAndNoValue()
    {
        // Arrange
        var sink = new MessageSink<string>();
        using var host = BuildHost(sink);
        await host.StartAsync();
        try
        {
            // Act
            await ProduceAsync(host, [
                new EventMessage<string, string>("order-1", "created"),
                EventMessage<string, string>.Tombstone("order-1"),
            ]);
            var received = await ReceiveAsync(sink, 2);

            // Assert
            Assert.Equal("created", received[0].Message);
            AssertTombstone(received[1], "order-1");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenTombstoneThroughADirectProducer_WhenProduced_ThenItArrivesWithTheKeyAndNoValue()
    {
        // Arrange
        var sink = new MessageSink<string>();
        using var host = BuildHost(sink, direct: true);
        await host.StartAsync();
        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>()
                .ProduceAsync(EventMessage<string, string>.Tombstone("order-1"));
            var received = await ReceiveAsync(sink, 1);

            // Assert
            AssertTombstone(received[0], "order-1");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenMessageWithANullValueThroughTheOutbox_WhenDelivered_ThenItIsATombstone()
    {
        // Arrange
        var sink = new MessageSink<string>();
        using var host = BuildHost(sink);
        await host.StartAsync();
        try
        {
            // Act
            await ProduceAsync(host, [new EventMessage<string, string>("order-1", null!)]);
            var received = await ReceiveAsync(sink, 1);

            // Assert
            AssertTombstone(received[0], "order-1");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenValuesAndTombstonesOfTwoKeysInterleaved_WhenDeliveredThroughTheOutbox_ThenEachKeyKeepsItsOrder()
    {
        // Arrange
        var sink = new MessageSink<string>();
        using var host = BuildHost(sink);
        await host.StartAsync();
        try
        {
            // Act
            await ProduceAsync(host, [
                new EventMessage<string, string>("k1", "k1-a"),
                new EventMessage<string, string>("k2", "k2-a"),
                EventMessage<string, string>.Tombstone("k1"),
                EventMessage<string, string>.Tombstone("k2"),
                new EventMessage<string, string>("k1", "k1-b"),
            ]);
            var received = await ReceiveAsync(sink, 5);

            // Assert
            Assert.Equal(["k1-a", null, "k1-b"], received.Where(m => KeyOf(m) == "k1").Select(m => m.Message));
            Assert.Equal(["k2-a", null], received.Where(m => KeyOf(m) == "k2").Select(m => m.Message));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenTombstoneInARolledBackTransaction_WhenTheOutboxDelivers_ThenItIsNeverDelivered()
    {
        // Arrange
        var sink = new MessageSink<string>();
        using var host = BuildHost(sink);
        await host.StartAsync();
        try
        {
            // Act: the rolled-back tombstone would be delivered before the committed message of the same key
            await ProduceAsync(host, [EventMessage<string, string>.Tombstone("order-1")], commit: false);
            await ProduceAsync(host, [new EventMessage<string, string>("order-1", "kept")]);
            var received = await ReceiveAsync(sink, 1);

            // Assert
            Assert.Equal("kept", received[0].Message);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenTombstoneWithAHeader_WhenDeliveredThroughTheOutbox_ThenItCarriesTheHeaderAndTheHeadersEmitAdds()
    {
        // Arrange: Emit adds trace context and the source address while producing within a trace
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EmitActivitySourceNames.Outbox,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        var sink = new MessageSink<string>();
        using var host = BuildHost(sink);
        await host.StartAsync();
        try
        {
            // Act
            using (new Activity("tombstone-test").Start())
            {
                await ProduceAsync(host, [EventMessage<string, string>.Tombstone("order-1", [new("reason", "deleted")])]);
            }

            var received = await ReceiveAsync(sink, 1);

            // Assert
            AssertTombstone(received[0], "order-1");
            var headers = received[0].TransportContext.Headers;
            Assert.Contains(new KeyValuePair<string, string>("reason", "deleted"), headers);
            Assert.Contains(headers, h => h.Key == WellKnownHeaders.TraceParent);
            Assert.Contains(headers, h => h.Key == WellKnownHeaders.SourceAddress);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenTopicWithASchemaRegistryValueSerializer_WhenATombstoneIsDeliveredThroughTheOutbox_ThenItArrivesWithNoValue()
    {
        // Arrange: a null value never reaches the serializer
        var sink = new MessageSink<Order>();
        var topic = $"test-tombstone-schema-{Guid.NewGuid():N}";
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit =>
                {
                    ConfigurePersistence(emit, outbox => outbox.PollingInterval = TimeSpan.FromSeconds(1));
                    emit.AddKafka(kafka =>
                    {
                        kafka.ConfigureClient(config => config.BootstrapServers = BootstrapServers);
                        kafka.ConfigureSchemaRegistry(registry => registry.Url = SchemaRegistryUrl);
                        kafka.AutoProvision();
                        kafka.Topic<string, Order>(topic, t =>
                        {
                            t.SetUtf8KeySerializer();
                            t.SetUtf8KeyDeserializer();
                            t.SetJsonSchemaValueSerializer();
                            t.SetJsonSchemaValueDeserializer();
                            t.Producer();
                            t.ConsumerGroup($"group-{Guid.NewGuid():N}", group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.AddConsumer<SinkConsumer<Order>>();
                            });
                        });
                    });
                });
            })
            .Build();
        await host.StartAsync();
        try
        {
            // Act
            await ProduceAsync(host, [
                new EventMessage<string, Order>("order-1", new Order("order-1")),
                EventMessage<string, Order>.Tombstone("order-1"),
            ]);
            var first = await sink.WaitForMessageAsync(ReceiveTimeout);
            var second = await sink.WaitForMessageAsync(ReceiveTimeout);

            // Assert
            Assert.Equal(new Order("order-1"), first.Message);
            Assert.Null(second.Message);
            Assert.Null(((KafkaTransportContext<string>)second.TransportContext).RawValue);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// The message type of the schema registry topic.
    /// </summary>
    public sealed record Order(string Id);

    // ── Helpers ──

    private IHost BuildHost(MessageSink<string> sink, bool direct = false)
    {
        var topicName = $"test-tombstone-{Guid.NewGuid():N}";
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit =>
                {
                    ConfigurePersistence(emit, outbox => outbox.PollingInterval = TimeSpan.FromSeconds(1));
                    emit.AddKafka(kafka =>
                    {
                        kafka.ConfigureClient(config => config.BootstrapServers = BootstrapServers);
                        kafka.AutoProvision();
                        kafka.Topic<string, string>(topicName, t =>
                        {
                            t.UseUtf8Serialization();
                            if (direct)
                            {
                                t.Producer(p => p.UseDirect());
                            }
                            else
                            {
                                t.Producer();
                            }

                            t.ConsumerGroup($"group-{Guid.NewGuid():N}", group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.AddConsumer<SinkConsumer<string>>();
                            });
                        });
                    });
                });
            })
            .Build();
    }

    /// <summary>
    /// Produces the messages through the outbox in one unit of work, committed or rolled back. Changes are saved
    /// after each message, which is how a provider that orders a save's entries by itself keeps a key's order.
    /// </summary>
    private async Task ProduceAsync<TValue>(IHost host, IReadOnlyList<EventMessage<string, TValue>> messages, bool commit = true)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var producer = services.GetRequiredService<IEventProducer<string, TValue>>();
        await using var transaction = await services.GetRequiredService<IUnitOfWork>().BeginAsync();
        foreach (var message in messages)
        {
            await producer.ProduceAsync(message);
            await FlushBeforeCommitAsync(services);
        }

        if (commit)
        {
            await transaction.CommitAsync();
        }
    }

    private static async Task<List<ConsumeContext<string>>> ReceiveAsync(MessageSink<string> sink, int count)
    {
        List<ConsumeContext<string>> received = [];
        while (received.Count < count)
        {
            received.Add(await sink.WaitForMessageAsync(ReceiveTimeout));
        }

        return received;
    }

    private static string? KeyOf(ConsumeContext<string> context) => ((KafkaTransportContext<string>)context.TransportContext).Key;

    private static void AssertTombstone(ConsumeContext<string> context, string key)
    {
        var transport = (KafkaTransportContext<string>)context.TransportContext;
        Assert.Null(context.Message);
        Assert.Null(transport.RawValue);
        Assert.Equal(key, transport.Key);
    }
}

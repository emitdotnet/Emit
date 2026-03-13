namespace Emit.MongoDB.Tests.Outbox;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Tests.TestInfrastructure;
using Emit.Testing;
using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Verifies that when outbox is enabled at the persistence level, producers that do not
/// call <c>UseOutbox()</c> send messages directly to Kafka (bypassing the outbox), while
/// producers that call <c>UseOutbox()</c> route through the transactional outbox.
/// </summary>
[Trait("Category", "Integration")]
public class MongoDbMixedProducerTests
    : IClassFixture<MongoDbContainerFixture>,
      IClassFixture<KafkaContainerFixture>,
      IAsyncLifetime
{
    private readonly string databaseName;
    private readonly IMongoClient mongoClient;
    private readonly MongoDbContainerFixture mongoFixture;
    private readonly KafkaContainerFixture kafkaFixture;

    public MongoDbMixedProducerTests(
        MongoDbContainerFixture mongoFixture,
        KafkaContainerFixture kafkaFixture)
    {
        this.mongoFixture = mongoFixture;
        this.kafkaFixture = kafkaFixture;
        databaseName = $"emit_mixed_{Guid.NewGuid():N}";
        mongoClient = new MongoClient(mongoFixture.ConnectionString);
    }

    public async Task InitializeAsync()
    {
        await mongoFixture.InitializeAsync();
        await kafkaFixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await mongoClient.DropDatabaseAsync(databaseName);
    }

    /// <summary>
    /// A producer without <c>UseOutbox()</c> sends directly to Kafka even when outbox
    /// is enabled at the persistence level. No transaction is required.
    /// </summary>
    [Fact]
    public async Task GivenDirectProducer_WhenProducingWithoutTransaction_ThenMessageDeliveredDirectly()
    {
        // Arrange
        var directTopic = $"test-direct-{Guid.NewGuid():N}";
        var directSink = new MessageSink<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(directSink);
                services.AddEmit(emit =>
                {
                    emit.AddMongoDb(mongo =>
                    {
                        mongo.Configure((_, ctx) =>
                        {
                            ctx.Client = mongoClient;
                            ctx.Database = mongoClient.GetDatabase(databaseName);
                        });
                        mongo.UseOutbox(opts => opts.PollingInterval = TimeSpan.FromSeconds(1));
                    });

                    emit.AddKafka(kafka =>
                    {
                        kafka.ConfigureClient(config =>
                        {
                            config.BootstrapServers = kafkaFixture.BootstrapServers;
                        });
                        kafka.AutoProvision();

                        // Direct producer — no UseOutbox()
                        kafka.Topic<string, string>(directTopic, t =>
                        {
                            t.SetUtf8KeySerializer();
                            t.SetUtf8ValueSerializer();
                            t.SetUtf8KeyDeserializer();
                            t.SetUtf8ValueDeserializer();

                            t.Producer();
                            t.ConsumerGroup($"group-{Guid.NewGuid():N}", group =>
                            {
                                group.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;
                                group.AddConsumer<SinkConsumer<string>>();
                            });
                        });
                    });
                });
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce WITHOUT a transaction (direct mode should work without one)
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "direct-msg"));

            // Assert — message arrives at the consumer directly (not via outbox daemon)
            var ctx = await directSink.WaitForMessageAsync();
            Assert.Equal("direct-msg", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// With two producers on separate topics — one with <c>UseOutbox()</c> and one without —
    /// the direct producer sends immediately while the outbox producer delivers via the daemon
    /// after the transaction commits.
    /// </summary>
    [Fact]
    public async Task GivenMixedProducers_WhenProducing_ThenEachUsesCorrectDeliveryPath()
    {
        // Arrange
        var outboxTopic = $"test-outbox-{Guid.NewGuid():N}";
        var directTopic = $"test-direct-{Guid.NewGuid():N}";
        var outboxSink = new MessageSink<string>();
        var directSink = new MessageSink<byte[]>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(outboxSink);
                services.AddSingleton(directSink);
                services.AddEmit(emit =>
                {
                    emit.AddMongoDb(mongo =>
                    {
                        mongo.Configure((_, ctx) =>
                        {
                            ctx.Client = mongoClient;
                            ctx.Database = mongoClient.GetDatabase(databaseName);
                        });
                        mongo.UseOutbox(opts => opts.PollingInterval = TimeSpan.FromSeconds(1));
                    });

                    emit.AddKafka(kafka =>
                    {
                        kafka.ConfigureClient(config =>
                        {
                            config.BootstrapServers = kafkaFixture.BootstrapServers;
                        });
                        kafka.AutoProvision();

                        // Outbox producer
                        kafka.Topic<string, string>(outboxTopic, t =>
                        {
                            t.SetUtf8KeySerializer();
                            t.SetUtf8ValueSerializer();
                            t.SetUtf8KeyDeserializer();
                            t.SetUtf8ValueDeserializer();

                            t.Producer(p => p.UseOutbox());
                            t.ConsumerGroup($"group-{Guid.NewGuid():N}", group =>
                            {
                                group.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;
                                group.AddConsumer<SinkConsumer<string>>();
                            });
                        });

                        // Direct producer (different types to get distinct IEventProducer registration)
                        kafka.Topic<byte[], byte[]>(directTopic, t =>
                        {
                            t.SetByteArrayKeySerializer();
                            t.SetByteArrayValueSerializer();
                            t.SetByteArrayKeyDeserializer();
                            t.SetByteArrayValueDeserializer();

                            t.Producer();
                            t.ConsumerGroup($"group-{Guid.NewGuid():N}", group =>
                            {
                                group.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;
                                group.AddConsumer<SinkConsumer<byte[]>>();
                            });
                        });
                    });
                });
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce to the direct producer WITHOUT a transaction
            var directPayload = "direct-msg"u8.ToArray();
            using (var scope = host.Services.CreateScope())
            {
                var directProducer = scope.ServiceProvider.GetRequiredService<IEventProducer<byte[], byte[]>>();
                await directProducer.ProduceAsync(new EventMessage<byte[], byte[]>(directPayload, directPayload));
            }

            // Assert — direct message arrives immediately
            var directCtx = await directSink.WaitForMessageAsync();
            Assert.Equal(directPayload, directCtx.Message);

            // Act — produce to the outbox producer WITH a transaction
            using (var scope = host.Services.CreateScope())
            {
                var emitContext = scope.ServiceProvider.GetRequiredService<IEmitContext>();
                var outboxProducer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();

                await using var transaction = await emitContext.BeginMongoTransactionAsync(mongoClient);
                await outboxProducer.ProduceAsync(new EventMessage<string, string>("k", "outbox-msg"));
                await transaction.CommitAsync();
            }

            // Assert — outbox message arrives after daemon picks it up
            var outboxCtx = await outboxSink.WaitForMessageAsync();
            Assert.Equal("outbox-msg", outboxCtx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}

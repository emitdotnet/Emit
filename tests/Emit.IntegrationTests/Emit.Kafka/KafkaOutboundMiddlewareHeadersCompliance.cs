namespace Emit.Kafka.Tests;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.MongoDB;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Tests.TestInfrastructure;
using Emit.Testing;
using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaOutboundMiddlewareHeadersCompliance
    : OutboundMiddlewareHeadersCompliance,
      IClassFixture<KafkaContainerFixture>,
      IClassFixture<MongoDbContainerFixture>,
      IAsyncLifetime
{
    private readonly string databaseName;
    private readonly IMongoClient mongoClient;
    private readonly KafkaContainerFixture kafkaFixture;
    private readonly MongoDbContainerFixture mongoFixture;

    public KafkaOutboundMiddlewareHeadersCompliance(
        KafkaContainerFixture kafkaFixture,
        MongoDbContainerFixture mongoFixture)
    {
        this.kafkaFixture = kafkaFixture;
        this.mongoFixture = mongoFixture;
        databaseName = $"emit_mw_headers_{Guid.NewGuid():N}"[..30];
        mongoClient = new MongoClient(mongoFixture.ConnectionString);
    }

    public async Task InitializeAsync()
    {
        await kafkaFixture.InitializeAsync();
        await mongoFixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await mongoClient.DropDatabaseAsync(databaseName);
    }

    protected override void ConfigureEmit(EmitBuilder emit, string topic, string groupId, bool useOutbox)
    {
        emit.OutboundPipeline.Use<HeaderManipulationMiddleware>(MiddlewareLifetime.Singleton);

        if (useOutbox)
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
        }

        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config =>
            {
                config.BootstrapServers = kafkaFixture.BootstrapServers;
            });

            kafka.Topic<string, string>(topic, t =>
            {
                t.SetUtf8KeySerializer();
                t.SetUtf8ValueSerializer();
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.Producer(p =>
                {
                    if (useOutbox) p.UseOutbox();
                });
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<SinkConsumer<string>>();
                });
            });
        });
    }

    protected override async Task ProduceAsync(
        IServiceProvider services,
        EventMessage<string, string> message,
        bool useOutbox,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var producer = sp.GetRequiredService<IEventProducer<string, string>>();

        if (useOutbox)
        {
            var emitContext = sp.GetRequiredService<IEmitContext>();
            await using var transaction = await emitContext
                .BeginMongoTransactionAsync(mongoClient, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await producer.ProduceAsync(message, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await producer.ProduceAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }
}

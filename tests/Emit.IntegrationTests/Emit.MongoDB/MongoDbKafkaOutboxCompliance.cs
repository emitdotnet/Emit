namespace Emit.MongoDB.Tests.Outbox;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Tests.TestInfrastructure;
using Emit.Testing;
using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// MongoDB + Kafka implementation of <see cref="OutboxDeliveryCompliance"/>.
/// Verifies that the MongoDB transactional outbox delivers messages via Kafka.
/// </summary>
[Trait("Category", "Integration")]
public class MongoDbKafkaOutboxCompliance
    : OutboxDeliveryCompliance,
      IClassFixture<MongoDbContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private readonly string databaseName;
    private readonly IMongoClient mongoClient;
    private readonly MongoDbContainerFixture mongoFixture;
    private readonly KafkaContainerFixture kafkaFixture;

    public MongoDbKafkaOutboxCompliance(
        MongoDbContainerFixture mongoFixture,
        KafkaContainerFixture kafkaFixture)
    {
        this.mongoFixture = mongoFixture;
        this.kafkaFixture = kafkaFixture;
        databaseName = $"emit_outbox_{Guid.NewGuid():N}";
        mongoClient = new MongoClient(mongoFixture.ConnectionString);
    }

    /// <inheritdoc/>
    public override async Task InitializeAsync()
    {
        await mongoFixture.InitializeAsync();
        await kafkaFixture.InitializeAsync();
    }

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        await mongoClient.DropDatabaseAsync(databaseName);
    }

    /// <inheritdoc/>
    protected override void ConfigureEmit(
        EmitBuilder emit,
        string topic,
        string groupId,
        TimeSpan pollingInterval)
    {
        emit.AddMongoDb(mongo =>
        {
            mongo.Configure((_, ctx) =>
            {
                ctx.Client = mongoClient;
                ctx.Database = mongoClient.GetDatabase(databaseName);
            });
            mongo.UseOutbox(opts => opts.PollingInterval = pollingInterval);
        });

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

                t.Producer(p => p.UseOutbox());
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<SinkConsumer<string>>();
                });
            });
        });
    }

    /// <inheritdoc/>
    protected override async Task ProduceTransactionallyAsync(
        IServiceProvider services,
        string key,
        string value,
        bool commit,
        CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var emitContext = sp.GetRequiredService<IEmitContext>();
        var producer = sp.GetRequiredService<IEventProducer<string, string>>();

        await using var transaction = await emitContext.BeginMongoTransactionAsync(mongoClient, cancellationToken: ct)
            .ConfigureAwait(false);

        await producer.ProduceAsync(new EventMessage<string, string>(key, value), ct)
            .ConfigureAwait(false);

        if (commit)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        else
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
        }
    }
}

namespace Emit.MongoDB.Tests.Outbox;

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

[Trait("Category", "Integration")]
public class MongoDbUnitOfWorkCompliance
    : UnitOfWorkCompliance,
      IClassFixture<MongoDbContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private readonly string databaseName;
    private readonly IMongoClient mongoClient;
    private readonly MongoDbContainerFixture mongoFixture;
    private readonly KafkaContainerFixture kafkaFixture;

    public MongoDbUnitOfWorkCompliance(
        MongoDbContainerFixture mongoFixture,
        KafkaContainerFixture kafkaFixture)
    {
        this.mongoFixture = mongoFixture;
        this.kafkaFixture = kafkaFixture;
        databaseName = $"emit_uow_{Guid.NewGuid():N}";
        mongoClient = new MongoClient(mongoFixture.ConnectionString);
    }

    public override async Task InitializeAsync()
    {
        await mongoFixture.InitializeAsync();
        await kafkaFixture.InitializeAsync();
    }

    public override async Task DisposeAsync()
    {
        await mongoClient.DropDatabaseAsync(databaseName);
    }

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
            kafka.AutoProvision();

            kafka.Topic<string, string>(topic, t =>
            {
                t.SetUtf8KeySerializer();
                t.SetUtf8ValueSerializer();
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.Producer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<SinkConsumer<string>>();
                });
            });
        });
    }
}

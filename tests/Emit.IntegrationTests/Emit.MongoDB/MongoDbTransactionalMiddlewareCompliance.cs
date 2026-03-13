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

[Trait("Category", "Integration")]
public class MongoDbTransactionalMiddlewareCompliance
    : TransactionalMiddlewareCompliance,
      IClassFixture<MongoDbContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private readonly string databaseName;
    private readonly IMongoClient mongoClient;
    private readonly MongoDbContainerFixture mongoFixture;
    private readonly KafkaContainerFixture kafkaFixture;

    public MongoDbTransactionalMiddlewareCompliance(
        MongoDbContainerFixture mongoFixture,
        KafkaContainerFixture kafkaFixture)
    {
        this.mongoFixture = mongoFixture;
        this.kafkaFixture = kafkaFixture;
        databaseName = $"emit_txnmw_{Guid.NewGuid():N}";
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
        string inputTopic,
        string outputTopic,
        string groupId,
        TimeSpan pollingInterval,
        Type consumerType)
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

            kafka.Topic<string, string>(inputTopic, t =>
            {
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.OnError(error => error.Default(a => a.Retry(1, Backoff.None).Discard()));

                    if (consumerType == typeof(TransactionalProducingConsumer))
                        group.AddConsumer<TransactionalProducingConsumer>();
                    else if (consumerType == typeof(TransactionalThrowingConsumer))
                        group.AddConsumer<TransactionalThrowingConsumer>();
                    else if (consumerType == typeof(NonTransactionalSinkConsumer))
                        group.AddConsumer<NonTransactionalSinkConsumer>();
                    else if (consumerType == typeof(TransactionalRetryConsumer))
                        group.AddConsumer<TransactionalRetryConsumer>();
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

    protected override async Task ProduceDirectAsync(
        IServiceProvider services,
        string topic,
        string key,
        string value,
        CancellationToken ct = default)
    {
        using var producer = new ConfluentKafka.ProducerBuilder<string, string>(
            new ConfluentKafka.ProducerConfig { BootstrapServers = kafkaFixture.BootstrapServers })
            .Build();

        await producer.ProduceAsync(
            topic,
            new ConfluentKafka.Message<string, string> { Key = key, Value = value },
            ct);
    }
}

namespace Emit.MongoDB.Tests.Outbox;

using System.Text;
using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Models;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Tests.TestInfrastructure;
using Emit.Testing;
using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

[Trait("Category", "Integration")]
public class MongoDbProducerRoutingCompliance
    : ProducerRoutingCompliance,
      IClassFixture<MongoDbContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private readonly string databaseName;
    private readonly IMongoClient mongoClient;
    private readonly MongoDbContainerFixture mongoFixture;
    private readonly KafkaContainerFixture kafkaFixture;
    private string? currentOutboxTopic;

    public MongoDbProducerRoutingCompliance(
        MongoDbContainerFixture mongoFixture,
        KafkaContainerFixture kafkaFixture)
    {
        this.mongoFixture = mongoFixture;
        this.kafkaFixture = kafkaFixture;
        databaseName = $"emit_route_{Guid.NewGuid():N}";
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
        string outboxTopic,
        string directTopic,
        string groupId,
        TimeSpan pollingInterval)
    {
        currentOutboxTopic = outboxTopic;

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

            kafka.Topic<string, string>(outboxTopic, t =>
            {
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.ConsumerGroup($"{groupId}-outbox", group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<OutboxTopicSinkConsumer>();
                });
            });

            kafka.Topic<string, string>(directTopic, t =>
            {
                t.SetUtf8KeySerializer();
                t.SetUtf8ValueSerializer();
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.Producer(p => p.UseDirect());
                t.ConsumerGroup($"{groupId}-direct", group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<DirectTopicSinkConsumer>();
                });
            });
        });
    }

    protected override async Task ProduceViaOutboxAsync(
        IServiceProvider services,
        string key,
        string value,
        CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
        var repository = sp.GetRequiredService<IOutboxRepository>();

        await using var tx = await unitOfWork.BeginAsync(ct);

        var keyBytes = Encoding.UTF8.GetBytes(key);
        await repository.EnqueueAsync(new OutboxEntry
        {
            SystemId = "kafka",
            Destination = $"kafka://localhost/{currentOutboxTopic}",
            GroupKey = $"kafka:{currentOutboxTopic}:{Convert.ToBase64String(keyBytes)}",
            Body = Encoding.UTF8.GetBytes(value),
            EnqueuedAt = DateTime.UtcNow,
            Properties = new Dictionary<string, string> { ["key"] = Convert.ToBase64String(keyBytes) },
        }, ct);

        await tx.CommitAsync(ct);
    }
}

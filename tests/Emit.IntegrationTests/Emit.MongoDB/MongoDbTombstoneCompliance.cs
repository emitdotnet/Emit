namespace Emit.MongoDB.Tests.Outbox;

using Emit.Configuration;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Tests.TestInfrastructure;
using global::MongoDB.Driver;
using Xunit;

/// <summary>
/// MongoDB + Kafka implementation of <see cref="TombstoneCompliance"/>.
/// </summary>
[Trait("Category", "Integration")]
public class MongoDbTombstoneCompliance(
    MongoDbContainerFixture mongoFixture,
    KafkaContainerFixture kafkaFixture)
    : TombstoneCompliance,
      IClassFixture<MongoDbContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private readonly string databaseName = $"emit_tombstone_{Guid.NewGuid():N}";
    private readonly IMongoClient mongoClient = new MongoClient(mongoFixture.ConnectionString);

    /// <inheritdoc/>
    protected override string BootstrapServers => kafkaFixture.BootstrapServers;

    /// <inheritdoc/>
    protected override string SchemaRegistryUrl => kafkaFixture.SchemaRegistryUrl;

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        await mongoClient.DropDatabaseAsync(databaseName);
    }

    /// <inheritdoc/>
    protected override void ConfigurePersistence(EmitBuilder emit, Action<OutboxOptions> configureOutbox)
    {
        emit.AddMongoDb(mongo =>
        {
            mongo.Configure((_, ctx) =>
            {
                ctx.Client = mongoClient;
                ctx.Database = mongoClient.GetDatabase(databaseName);
            });
            mongo.UseOutbox(configureOutbox);
        });
    }
}

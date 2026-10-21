namespace Emit.MongoDB.Tests.Outbox;

using Emit.Configuration;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Tests.TestInfrastructure;
using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// MongoDB + Kafka implementation of <see cref="OutboxDeliveryCompliance"/>.
/// Verifies that the MongoDB transactional outbox delivers messages via Kafka.
/// </summary>
[Trait("Category", "Integration")]
public class MongoDbOutboxDeliveryCompliance(
    MongoDbContainerFixture mongoFixture,
    KafkaContainerFixture kafkaFixture)
    : OutboxDeliveryCompliance,
      IClassFixture<MongoDbContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private readonly string databaseName = $"emit_outbox_{Guid.NewGuid():N}";
    private readonly IMongoClient mongoClient = new MongoClient(mongoFixture.ConnectionString);

    /// <inheritdoc/>
    protected override string BootstrapServers => kafkaFixture.BootstrapServers;

    /// <inheritdoc/>
    public override Task InitializeAsync() => Task.CompletedTask;

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

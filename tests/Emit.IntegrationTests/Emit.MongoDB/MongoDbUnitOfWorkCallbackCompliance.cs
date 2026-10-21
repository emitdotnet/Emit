namespace Emit.MongoDB.Tests.Outbox;

using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Tests.TestInfrastructure;
using global::MongoDB.Driver;
using Xunit;

/// <summary>
/// MongoDB + Kafka implementation of <see cref="UnitOfWorkCallbackCompliance"/>.
/// </summary>
[Trait("Category", "Integration")]
public class MongoDbUnitOfWorkCallbackCompliance(
    MongoDbContainerFixture mongoFixture,
    KafkaContainerFixture kafkaFixture)
    : UnitOfWorkCallbackCompliance,
      IClassFixture<MongoDbContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private readonly string databaseName = $"emit_uowcb_{Guid.NewGuid():N}";
    private readonly IMongoClient mongoClient = new MongoClient(mongoFixture.ConnectionString);

    protected override string BootstrapServers => kafkaFixture.BootstrapServers;

    public override async Task DisposeAsync()
    {
        await mongoClient.DropDatabaseAsync(databaseName);
    }

    protected override void ConfigurePersistence(EmitBuilder emit, bool outbox)
    {
        emit.AddMongoDb(mongo =>
        {
            mongo.Configure((_, ctx) =>
            {
                ctx.Client = mongoClient;
                ctx.Database = mongoClient.GetDatabase(databaseName);
            });
            if (outbox)
            {
                mongo.UseOutbox();
            }
        });
    }
}

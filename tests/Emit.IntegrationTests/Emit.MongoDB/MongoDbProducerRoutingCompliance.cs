namespace Emit.MongoDB.Tests.Outbox;

using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Tests.TestInfrastructure;
using global::MongoDB.Driver;
using Xunit;

[Trait("Category", "Integration")]
public class MongoDbProducerRoutingCompliance(
    MongoDbContainerFixture mongoFixture,
    KafkaContainerFixture kafkaFixture)
    : ProducerRoutingCompliance,
      IClassFixture<MongoDbContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private readonly string databaseName = $"emit_route_{Guid.NewGuid():N}";
    private readonly IMongoClient mongoClient = new MongoClient(mongoFixture.ConnectionString);

    /// <inheritdoc />
    protected override string BootstrapServers => kafkaFixture.BootstrapServers;

    /// <inheritdoc />
    public override async Task InitializeAsync()
    {
        await mongoFixture.InitializeAsync();
        await kafkaFixture.InitializeAsync();
    }

    /// <inheritdoc />
    public override async Task DisposeAsync()
    {
        await mongoClient.DropDatabaseAsync(databaseName);
    }

    /// <inheritdoc />
    protected override void ConfigurePersistence(EmitBuilder emit, TimeSpan pollingInterval)
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
    }
}

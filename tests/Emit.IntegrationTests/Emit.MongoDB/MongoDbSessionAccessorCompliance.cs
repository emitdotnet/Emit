namespace Emit.MongoDB.Tests.Outbox;

using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Tests.TestInfrastructure;
using global::MongoDB.Driver;
using Xunit;

[Trait("Category", "Integration")]
public class MongoDbSessionAccessorCompliance
    : MongoSessionAccessorCompliance,
      IClassFixture<MongoDbContainerFixture>
{
    private readonly string databaseName;
    private readonly IMongoClient mongoClient;
    private readonly MongoDbContainerFixture mongoFixture;

    public MongoDbSessionAccessorCompliance(MongoDbContainerFixture mongoFixture)
    {
        this.mongoFixture = mongoFixture;
        databaseName = $"emit_session_{Guid.NewGuid():N}";
        mongoClient = new MongoClient(mongoFixture.ConnectionString);
    }

    public override async Task InitializeAsync()
    {
        await mongoFixture.InitializeAsync();
    }

    public override async Task DisposeAsync()
    {
        await mongoClient.DropDatabaseAsync(databaseName);
    }

    protected override void ConfigureEmit(EmitBuilder emit)
    {
        emit.AddMongoDb(mongo =>
        {
            mongo.Configure((_, ctx) =>
            {
                ctx.Client = mongoClient;
                ctx.Database = mongoClient.GetDatabase(databaseName);
            });
            mongo.UseOutbox();
        });
    }
}

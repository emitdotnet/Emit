namespace Emit.MongoDB.Tests.LeaderElection;

using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Tests.TestInfrastructure;
using global::MongoDB.Driver;
using Xunit;

/// <summary>
/// MongoDB implementation of <see cref="ServiceCoordinationCompliance"/>.
/// </summary>
[Trait("Category", "Integration")]
public class MongoDbServiceCoordinationCompliance(MongoDbContainerFixture mongoFixture)
    : ServiceCoordinationCompliance, IClassFixture<MongoDbContainerFixture>
{
    private readonly string databaseName = $"emit_svc_{Guid.NewGuid():N}";
    private readonly IMongoClient mongoClient = new MongoClient(mongoFixture.ConnectionString);

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        await mongoClient.DropDatabaseAsync(databaseName);
    }

    /// <inheritdoc/>
    protected override void ConfigurePersistence(EmitBuilder emit)
    {
        emit.AddMongoDb(mongo =>
        {
            mongo.Configure((_, ctx) =>
            {
                ctx.Client = mongoClient;
                ctx.Database = mongoClient.GetDatabase(databaseName);
            });
        });
    }
}

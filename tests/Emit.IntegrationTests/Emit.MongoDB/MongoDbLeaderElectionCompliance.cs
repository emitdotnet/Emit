namespace Emit.MongoDB.Tests.LeaderElection;

using Emit.Abstractions.LeaderElection;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.MongoDB;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Tests.TestInfrastructure;
using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// MongoDB integration tests for <see cref="MongoDbLeaderElectionPersistence"/>.
/// </summary>
[Trait("Category", "Integration")]
public class MongoDbLeaderElectionCompliance : LeaderElectionCompliance, IClassFixture<MongoDbContainerFixture>
{
    private readonly string databaseName;
    private readonly IMongoClient mongoClient;
    private readonly ILeaderElectionPersistence persistence;

    public MongoDbLeaderElectionCompliance(MongoDbContainerFixture containerFixture)
    {
        databaseName = $"emit_le_test_{Guid.NewGuid():N}";
        mongoClient = new MongoClient(containerFixture.ConnectionString);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEmit(builder =>
        {
            builder.AddMongoDb(mongo =>
            {
                mongo.Configure((_, ctx) =>
                {
                    ctx.Client = mongoClient;
                    ctx.Database = mongoClient.GetDatabase(databaseName);
                });
            });
        });

        var sp = services.BuildServiceProvider();
        persistence = sp.GetRequiredService<ILeaderElectionPersistence>();
    }

    /// <inheritdoc/>
    protected override ILeaderElectionPersistence Persistence => persistence;

    /// <inheritdoc/>
    public override Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        await mongoClient.DropDatabaseAsync(databaseName);
    }
}

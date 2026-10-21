namespace Emit.MongoDB.Tests.LeaderElection;

using Emit.Abstractions.LeaderElection;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.MongoDB;
using Emit.MongoDB.Configuration;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Tests.TestInfrastructure;
using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// MongoDB integration tests for <see cref="MongoDbLeaderElectionPersistence"/>.
/// </summary>
[Trait("Category", "Integration")]
public class MongoDbLeaderElectionCompliance : DatabaseLeaderElectionCompliance, IClassFixture<MongoDbContainerFixture>
{
    private readonly string databaseName;
    private readonly ServiceProvider serviceProvider;
    private readonly string applicationName = $"emit-le-test-{Guid.NewGuid():N}";
    private readonly IMongoClient mongoClient;
    private readonly ILeaderElectionPersistence persistence;

    public MongoDbLeaderElectionCompliance(MongoDbContainerFixture containerFixture)
    {
        databaseName = $"emit_le_test_{Guid.NewGuid():N}";
        var settings = MongoClientSettings.FromConnectionString(containerFixture.ConnectionString);
        settings.ApplicationName = applicationName;
        mongoClient = new MongoClient(settings);

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

        serviceProvider = services.BuildServiceProvider();
        persistence = serviceProvider.GetRequiredService<ILeaderElectionPersistence>();
    }

    /// <inheritdoc/>
    protected override ILeaderElectionPersistence Persistence => persistence;

    /// <inheritdoc/>
    protected override async Task<IAsyncDisposable> BlockLeaseAsync(string serviceName)
    {
        // The failCommand fail point stalls this client's findAndModify commands, which update the lease,
        // and then fails them so they never run late
        await ConfigureFailPointAsync(new BsonDocument
        {
            { "mode", "alwaysOn" },
            {
                "data", new BsonDocument
                {
                    { "failCommands", new BsonArray { "findAndModify" } },
                    { "appName", applicationName },
                    { "blockConnection", true },
                    { "blockTimeMS", 5000 },
                    { "errorCode", 2 },
                }
            },
        });
        return new FailPoint(this);
    }

    private Task ConfigureFailPointAsync(BsonDocument settings) =>
        mongoClient.GetDatabase("admin").RunCommandAsync<BsonDocument>(
            new BsonDocument("configureFailPoint", "failCommand").AddRange(settings));

    /// <inheritdoc/>
    public override Task InitializeAsync() => Task.CompletedTask;

    private sealed class FailPoint(MongoDbLeaderElectionCompliance compliance) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() =>
            await compliance.ConfigureFailPointAsync(new BsonDocument("mode", "off"));
    }

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        await serviceProvider.DisposeAsync();
        await mongoClient.DropDatabaseAsync(databaseName);
    }
}

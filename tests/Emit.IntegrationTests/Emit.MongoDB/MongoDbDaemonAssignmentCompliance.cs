namespace Emit.MongoDB.Tests.Daemon;

using Emit.Abstractions.Daemon;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.MongoDB;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Tests.TestInfrastructure;
using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// MongoDB integration tests for <see cref="MongoDbDaemonAssignmentPersistence"/>.
/// </summary>
[Trait("Category", "Integration")]
public class MongoDbDaemonAssignmentCompliance : DaemonAssignmentCompliance, IClassFixture<MongoDbContainerFixture>
{
    private readonly string databaseName;
    private readonly ServiceProvider serviceProvider;
    private readonly IMongoClient mongoClient;
    private readonly IDaemonAssignmentPersistence persistence;

    public MongoDbDaemonAssignmentCompliance(MongoDbContainerFixture containerFixture)
    {
        databaseName = $"emit_da_test_{Guid.NewGuid():N}";
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

        serviceProvider = services.BuildServiceProvider();
        persistence = serviceProvider.GetRequiredService<IDaemonAssignmentPersistence>();
    }

    /// <inheritdoc/>
    protected override IDaemonAssignmentPersistence Persistence => persistence;

    /// <inheritdoc/>
    public override Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        await serviceProvider.DisposeAsync();
        await mongoClient.DropDatabaseAsync(databaseName);
    }
}

namespace Emit.MongoDB.Tests.DistributedLock;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.MongoDB;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Tests.TestInfrastructure;
using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// MongoDB integration tests for <see cref="MongoDbDistributedLockProvider"/>.
/// </summary>
[Trait("Category", "Integration")]
public class MongoDbDistributedLockCompliance : DistributedLockCompliance, IClassFixture<MongoDbContainerFixture>
{
    private readonly string databaseName;
    private readonly IMongoClient mongoClient;
    private readonly IDistributedLockProvider lockProvider;

    public MongoDbDistributedLockCompliance(MongoDbContainerFixture containerFixture)
    {
        databaseName = $"emit_lock_test_{Guid.NewGuid():N}";
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
                mongo.UseDistributedLock();
            });
        });

        var sp = services.BuildServiceProvider();
        lockProvider = sp.GetRequiredService<IDistributedLockProvider>();
    }

    /// <inheritdoc/>
    protected override IDistributedLockProvider LockProvider => lockProvider;

    /// <inheritdoc/>
    public override Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        await mongoClient.DropDatabaseAsync(databaseName);
    }
}

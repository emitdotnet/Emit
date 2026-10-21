namespace Emit.MongoDB.Tests.Configuration;

using Emit.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Emit.MongoDB.Configuration;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Tests.TestInfrastructure;
using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Integration tests for how Emit configures the MongoDB collections it owns: read preferences and indexes.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MongoDbCollectionConfigurationTests(MongoDbContainerFixture containerFixture)
    : IClassFixture<MongoDbContainerFixture>, IAsyncLifetime
{
    private readonly string databaseName = $"emit_config_test_{Guid.NewGuid():N}";

    [Fact]
    public void GivenClientThatReadsFromSecondaries_WhenEmitUsesItsCollections_ThenEveryCollectionReadsFromThePrimary()
    {
        // Arrange: a lagging secondary would show a node an assignment that already moved, or an outbox entry
        // that was already delivered and deleted
        var settings = MongoClientSettings.FromConnectionString(containerFixture.ConnectionString);
        settings.ReadPreference = ReadPreference.SecondaryPreferred;
        using var provider = BuildServices(new MongoClient(settings));

        // Act
        var context = provider.GetRequiredService<MongoDbContext>();

        // Assert
        Assert.All(
            new[]
            {
                context.LeaderCollection.Settings,
                context.NodeCollection.Settings,
                context.DaemonAssignmentCollection.Settings,
                context.OutboxCollection!.Settings,
                context.SequenceCollection!.Settings,
                context.LockCollection!.Settings,
            },
            collection => Assert.Equal(ReadPreference.Primary, collection.ReadPreference));
    }

    [Fact]
    public async Task GivenEmitCollections_WhenTheirIndexesAreCreated_ThenNodeRegistrationsAreIndexedOnlyOnFieldsAHeartbeatNeverRewrites()
    {
        // Arrange: a scan of an index whose key a heartbeat rewrites can skip a node that renewed during it, and the
        // leader moves the daemons of a node missing from its read at once
        using var provider = BuildServices(new MongoClient(containerFixture.ConnectionString));

        // Act
        var context = provider.GetRequiredService<MongoDbContext>();
        var indexes = await (await context.NodeCollection.Indexes.ListAsync()).ToListAsync();

        // Assert
        var keys = indexes.Select(index => index["key"].AsBsonDocument.Names.ToArray()).ToList();
        Assert.Contains(keys, key => key.SequenceEqual(["serviceName"]));
        Assert.All(keys, key => Assert.All(key, field => Assert.Contains(field, new[] { "_id", "serviceName" })));
    }

    /// <inheritdoc/>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public async Task DisposeAsync() =>
        await new MongoClient(containerFixture.ConnectionString).DropDatabaseAsync(databaseName);

    private ServiceProvider BuildServices(MongoClient client)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEmit(builder =>
        {
            builder.AddMongoDb(mongo =>
            {
                mongo.Configure((_, ctx) =>
                {
                    ctx.Client = client;
                    ctx.Database = client.GetDatabase(databaseName);
                });
                mongo.UseOutbox();
                mongo.UseDistributedLock();
            });
            builder.AddKafka(kafka => kafka.ConfigureClient(config => config.BootstrapServers = "localhost:9092"));
        });
        return services.BuildServiceProvider();
    }
}

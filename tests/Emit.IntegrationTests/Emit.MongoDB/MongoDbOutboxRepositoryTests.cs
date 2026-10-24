namespace Emit.MongoDB.Tests.Outbox;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Emit.Models;
using Emit.MongoDB.Configuration;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Tests.TestInfrastructure;
using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Integration tests for MongoDB-specific behaviour of the outbox repository.
/// </summary>
[Trait("Category", "Integration")]
[Collection(MongoDbFailPointCollection.Name)]
public sealed class MongoDbOutboxRepositoryTests : IClassFixture<MongoDbContainerFixture>, IAsyncLifetime
{
    private readonly string databaseName = $"emit_outbox_repo_{Guid.NewGuid():N}";
    private readonly string applicationName = $"emit-outbox-repo-{Guid.NewGuid():N}";
    private readonly IMongoClient mongoClient;
    private readonly ServiceProvider serviceProvider;

    public MongoDbOutboxRepositoryTests(MongoDbContainerFixture containerFixture)
    {
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
                mongo.UseOutbox();
            });
            builder.AddKafka(kafka => kafka.ConfigureClient(config => config.BootstrapServers = "localhost:9092"));
        });
        serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task GivenDeliveredEntries_WhenTheirDeleteCannotComplete_ThenNoneOfThemIsDeleted()
    {
        // Arrange: a delete that removed only some entries of a batch would deliver the rest again after the ones
        // already delivered, out of order; the commit of the delete is made to fail once
        var collection = serviceProvider.GetRequiredService<MongoDbContext>().OutboxCollection!;
        List<OutboxEntry> entries = [.. Enumerable.Range(1, 3).Select(Entry)];
        await collection.InsertManyAsync(entries);
        using var scope = serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        await ConfigureFailPointAsync(new BsonDocument
        {
            { "mode", new BsonDocument("times", 1) },
            {
                "data", new BsonDocument
                {
                    { "failCommands", new BsonArray { "commitTransaction" } },
                    { "appName", applicationName },
                    { "errorCode", 2 },
                }
            },
        });

        // Act
        var failure = await Record.ExceptionAsync(() => repository.DeleteAsync([.. entries.Select(e => e.Id!)]));

        // Assert
        Assert.NotNull(failure);
        Assert.Equal(3, await collection.CountDocumentsAsync(FilterDefinition<OutboxEntry>.Empty));
    }

    /// <inheritdoc/>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        await ConfigureFailPointAsync(new BsonDocument("mode", "off"));
        await mongoClient.DropDatabaseAsync(databaseName);
        await serviceProvider.DisposeAsync();
    }

    private static OutboxEntry Entry(int sequence) =>
        new()
        {
            Id = ObjectId.GenerateNewId(),
            ServiceName = "service",
            SystemId = "kafka",
            Destination = "kafka://localhost/topic",
            GroupKey = "kafka:topic:key",
            Sequence = sequence,
            Body = [1],
            Properties = [],
            Headers = [],
        };

    private Task ConfigureFailPointAsync(BsonDocument settings) =>
        mongoClient.GetDatabase("admin").RunCommandAsync<BsonDocument>(
            new BsonDocument("configureFailPoint", "failCommand").AddRange(settings));
}

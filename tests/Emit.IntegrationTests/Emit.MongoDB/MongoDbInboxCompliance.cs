namespace Emit.MongoDB.Tests.Inbox;

using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Tests.TestInfrastructure;
using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

/// <summary>
/// MongoDB + Kafka implementation of <see cref="InboxCompliance"/>.
/// </summary>
[Trait("Category", "Integration")]
public class MongoDbInboxCompliance(
    MongoDbContainerFixture mongoFixture,
    KafkaContainerFixture kafkaFixture)
    : InboxCompliance,
      IClassFixture<MongoDbContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private readonly string databaseName = $"emit_inbox_{Guid.NewGuid():N}";
    private readonly IMongoClient mongoClient = new MongoClient(mongoFixture.ConnectionString);

    /// <inheritdoc/>
    protected override string BootstrapServers => kafkaFixture.BootstrapServers;

    /// <inheritdoc/>
    public override Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        await mongoClient.DropDatabaseAsync(databaseName);
    }

    /// <inheritdoc/>
    protected override void ConfigurePersistence(EmitBuilder emit, TimeSpan retention)
    {
        emit.AddMongoDb(mongo =>
        {
            mongo.Configure((_, ctx) =>
            {
                ctx.Client = mongoClient;
                ctx.Database = mongoClient.GetDatabase(databaseName);
            });
            mongo.UseInbox(options => options.Retention = retention);
        });
    }

    /// <inheritdoc/>
    protected override async Task<IReadOnlyList<StoredInboxRecord>> ReadInboxRecordsAsync(string messageId)
    {
        var documents = await mongoClient.GetDatabase(databaseName)
            .GetCollection<BsonDocument>("emit.inbox")
            .Find(new BsonDocument("messageId", messageId))
            .ToListAsync();

        return documents
            .Select(d => new StoredInboxRecord(
                d["handler"].AsString,
                d["handledAt"].ToUniversalTime(),
                d["expiresAt"].ToUniversalTime()))
            .ToList();
    }
}

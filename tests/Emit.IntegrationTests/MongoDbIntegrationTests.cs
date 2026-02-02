namespace Emit.IntegrationTests;

using Emit.Abstractions;
using Emit.Persistence.MongoDB;
using Emit.Persistence.MongoDB.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Xunit;

/// <summary>
/// Integration tests for <see cref="MongoDbOutboxRepository"/>.
/// </summary>
/// <remarks>
/// <para>
/// These tests require a running MongoDB instance. The connection string is read from
/// the MONGODB_CONNECTION_STRING environment variable.
/// </para>
/// <para>
/// Each test run creates a unique database to ensure test isolation.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public class MongoDbIntegrationTests : BaseIntegrationTest
{
    private const string ConnectionStringEnvVar = "MONGODB_CONNECTION_STRING";
    private const string DefaultConnectionString = "mongodb://localhost:27017";

    private readonly string databaseName;
    private readonly string connectionString;
    private readonly IMongoClient mongoClient;
    private readonly MongoDbOutboxRepository repository;

    public MongoDbIntegrationTests()
    {
        connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvVar)
            ?? DefaultConnectionString;

        // Create a unique database for each test run to ensure isolation
        databaseName = $"emit_test_{Guid.NewGuid():N}";

        mongoClient = new MongoClient(connectionString);

        var options = Options.Create(new MongoDbOptions
        {
            ConnectionString = connectionString,
            DatabaseName = databaseName,
            CollectionName = "outbox",
            CounterCollectionName = "outbox_sequences",
            LeaseCollectionName = "outbox_lease"
        });

        var logger = new LoggerFactory()
            .CreateLogger<MongoDbOutboxRepository>();

        repository = new MongoDbOutboxRepository(mongoClient, options, logger);
    }

    /// <inheritdoc/>
    protected override IOutboxRepository Repository => repository;

    /// <inheritdoc/>
    protected override string ConnectionString => connectionString;

    /// <inheritdoc/>
    public override async Task InitializeAsync()
    {
        // Ensure indexes are created before running tests
        await repository.EnsureIndexesAsync();
    }

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        // Clean up the test database after tests complete
        await mongoClient.DropDatabaseAsync(databaseName);
    }

    #region MongoDB-Specific Tests

    [Fact]
    public async Task GivenMongoDb_WhenEnsureIndexesAsync_ThenIndexesAreCreated()
    {
        // Arrange
        var database = mongoClient.GetDatabase(databaseName);
        var collection = database.GetCollection<MongoDB.Bson.BsonDocument>("outbox");

        // Act
        await repository.EnsureIndexesAsync();

        // Assert
        var indexes = await collection.Indexes.ListAsync();
        var indexList = await indexes.ToListAsync();

        // Should have at least 4 indexes: _id (default) + 3 custom indexes
        Assert.True(indexList.Count >= 4);
    }

    #endregion
}

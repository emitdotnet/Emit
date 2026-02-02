namespace Emit.IntegrationTests;

using Emit.Abstractions;
using Emit.Persistence.PostgreSQL;
using Emit.Persistence.PostgreSQL.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

/// <summary>
/// Integration tests for <see cref="PostgreSqlOutboxRepository"/>.
/// </summary>
/// <remarks>
/// <para>
/// These tests require a running PostgreSQL instance. The connection string is read from
/// the POSTGRES_CONNECTION_STRING environment variable.
/// </para>
/// <para>
/// Each test class instance creates a unique database to ensure test isolation
/// and support parallel test execution.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public class PostgreSqlIntegrationTests : BaseIntegrationTest
{
    private const string ConnectionStringEnvVar = "POSTGRES_CONNECTION_STRING";
    private const string DefaultConnectionString = "Host=localhost;Database=postgres;Username=postgres;Password=password";

    private readonly string databaseName;
    private readonly string baseConnectionString;
    private readonly string testConnectionString;
    private readonly IServiceProvider serviceProvider;
    private readonly PostgreSqlOutboxRepository repository;
    private readonly IDbContextFactory<OutboxDbContext> dbContextFactory;

    public PostgreSqlIntegrationTests()
    {
        baseConnectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvVar)
            ?? DefaultConnectionString;

        // Create a unique database for each test class instance
        databaseName = $"emit_test_{Guid.NewGuid():N}".Substring(0, 30);

        // Build connection string for the test database
        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName
        };
        testConnectionString = builder.ConnectionString;

        var services = new ServiceCollection();

        var options = new PostgreSqlOptions
        {
            ConnectionString = testConnectionString,
            TableName = "outbox",
            LeaseTableName = "outbox_lease"
        };

        // Register options - required by OutboxDbContext
        services.Configure<PostgreSqlOptions>(opt =>
        {
            opt.ConnectionString = options.ConnectionString;
            opt.TableName = options.TableName;
            opt.LeaseTableName = options.LeaseTableName;
        });

        services.AddLogging();

        // Register DbContextFactory with the test database connection
        services.AddDbContextFactory<OutboxDbContext>((sp, dbOptions) =>
        {
            dbOptions.UseNpgsql(testConnectionString);
        });

        serviceProvider = services.BuildServiceProvider();

        dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<OutboxDbContext>>();

        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger<PostgreSqlOutboxRepository>();

        repository = new PostgreSqlOutboxRepository(
            dbContextFactory,
            Options.Create(options),
            logger);
    }

    /// <inheritdoc/>
    protected override IOutboxRepository Repository => repository;

    /// <inheritdoc/>
    protected override string ConnectionString => testConnectionString;

    /// <inheritdoc/>
    public override async Task InitializeAsync()
    {
        // Create the test database
        await using var adminConnection = new NpgsqlConnection(baseConnectionString);
        await adminConnection.OpenAsync();

        await using var createDbCmd = adminConnection.CreateCommand();
        createDbCmd.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await createDbCmd.ExecuteNonQueryAsync();

        // Ensure schema is created
        await repository.EnsureSchemaAsync();
    }

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        // Dispose the service provider first to close all connections
        if (serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        // Drop the test database
        await using var adminConnection = new NpgsqlConnection(baseConnectionString);
        await adminConnection.OpenAsync();

        // Terminate existing connections to the database
        await using var terminateCmd = adminConnection.CreateCommand();
        terminateCmd.CommandText = $@"
            SELECT pg_terminate_backend(pg_stat_activity.pid)
            FROM pg_stat_activity
            WHERE pg_stat_activity.datname = '{databaseName}'
            AND pid <> pg_backend_pid()";
        await terminateCmd.ExecuteNonQueryAsync();

        // Drop the database
        await using var dropDbCmd = adminConnection.CreateCommand();
        dropDbCmd.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\"";
        await dropDbCmd.ExecuteNonQueryAsync();
    }

    #region PostgreSQL-Specific Tests

    [Fact]
    public async Task GivenPostgreSql_WhenEnsureSchemaAsync_ThenTablesAreCreated()
    {
        // Arrange
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();

        // Act - schema already created in InitializeAsync, but calling again should be idempotent
        await repository.EnsureSchemaAsync();

        // Assert - verify table exists by checking information_schema
        await using var connection = new NpgsqlConnection(testConnectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT EXISTS (
                SELECT FROM information_schema.tables
                WHERE table_name = 'outbox'
            )";

        var exists = (bool)(await cmd.ExecuteScalarAsync())!;
        Assert.True(exists);
    }

    [Fact]
    public async Task GivenPostgreSql_WhenEnqueueMultipleEntries_ThenSequenceAutoIncrements()
    {
        // Arrange
        var uniqueGroupKey = $"pg-seq-test-{Guid.NewGuid():N}";

        var entry1 = CreateTestEntry(groupKey: uniqueGroupKey);
        var entry2 = CreateTestEntry(groupKey: uniqueGroupKey);

        // Act
        await Repository.EnqueueAsync(entry1, transaction: null);
        await Repository.EnqueueAsync(entry2, transaction: null);

        // Assert
        // PostgreSQL uses IDENTITY columns, so sequences auto-increment
        Assert.NotEqual(entry1.Sequence, entry2.Sequence);
        Assert.True(entry2.Sequence > entry1.Sequence);
    }

    #endregion
}

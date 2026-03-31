namespace Emit.EntityFrameworkCore.Tests.TestInfrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

/// <summary>
/// Manages the lifecycle of a per-test PostgreSQL database: creation, schema migration,
/// and teardown. Eliminates the repeated CREATE/DROP DATABASE boilerplate across EF Core
/// integration tests.
/// </summary>
internal sealed class PostgreSqlTestDatabase
{
    private PostgreSqlTestDatabase(string databaseName, string connectionString, string adminConnectionString)
    {
        DatabaseName = databaseName;
        ConnectionString = connectionString;
        AdminConnectionString = adminConnectionString;
    }

    public string DatabaseName { get; }
    public string ConnectionString { get; }
    private string AdminConnectionString { get; }

    /// <summary>
    /// Creates a new test database with a unique name, runs EF Core schema migration, and
    /// returns a handle for configuring services and tearing down after the test.
    /// </summary>
    /// <param name="adminConnectionString">
    /// The connection string to the PostgreSQL server (not a specific database).
    /// Typically from <see cref="PostgreSqlContainerFixture.ConnectionString"/>.
    /// </param>
    /// <param name="prefix">Short prefix for the database name (e.g., "txnrouter").</param>
    public static async Task<PostgreSqlTestDatabase> CreateAsync(string adminConnectionString, string prefix)
    {
        var databaseName = $"emit_{prefix}_{Guid.NewGuid():N}"[..30];

        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = databaseName,
            MaxPoolSize = 5,
            MinPoolSize = 0
        };
        var connectionString = builder.ConnectionString;

        // Create the database
        await using var adminConnection = new NpgsqlConnection(adminConnectionString);
        await adminConnection.OpenAsync();

        await using var createCmd = adminConnection.CreateCommand();
        createCmd.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await createCmd.ExecuteNonQueryAsync();

        // Run EF Core schema migration
        var services = new ServiceCollection();
        services.AddDbContextFactory<IntegrationTestDbContext>(opts => opts.UseNpgsql(connectionString));
        await using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDbContextFactory<IntegrationTestDbContext>>();
        await using var dbContext = await factory.CreateDbContextAsync();
        await dbContext.Database.EnsureCreatedAsync();

        return new PostgreSqlTestDatabase(databaseName, connectionString, adminConnectionString);
    }

    /// <summary>
    /// Terminates all connections and drops the test database.
    /// </summary>
    public async Task DropAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(ConnectionString));

        await using var adminConnection = new NpgsqlConnection(AdminConnectionString);
        await adminConnection.OpenAsync();

        await using var terminateCmd = adminConnection.CreateCommand();
        terminateCmd.CommandText = $"""
            SELECT pg_terminate_backend(pg_stat_activity.pid)
            FROM pg_stat_activity
            WHERE pg_stat_activity.datname = '{DatabaseName}'
            AND pid <> pg_backend_pid()
            """;
        await terminateCmd.ExecuteNonQueryAsync();

        await using var dropCmd = adminConnection.CreateCommand();
        dropCmd.CommandText = $"DROP DATABASE IF EXISTS \"{DatabaseName}\"";
        await dropCmd.ExecuteNonQueryAsync();
    }
}

namespace Emit.EntityFrameworkCore.Tests.Daemon;

using Emit.Abstractions.Daemon;
using Emit.DependencyInjection;
using Emit.EntityFrameworkCore;
using Emit.EntityFrameworkCore.DependencyInjection;
using Emit.EntityFrameworkCore.Tests.TestInfrastructure;
using Emit.IntegrationTests.Integration.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

/// <summary>
/// PostgreSQL integration tests for <see cref="EfCoreDaemonAssignmentPersistence{TDbContext}"/>.
/// </summary>
[Trait("Category", "Integration")]
public class PostgreSqlDaemonAssignmentCompliance : DaemonAssignmentCompliance, IClassFixture<PostgreSqlContainerFixture>
{
    private readonly string adminConnectionString;
    private readonly string databaseName;
    private readonly string testConnectionString;
    private readonly ServiceProvider serviceProvider;
    private readonly IDbContextFactory<IntegrationTestDbContext> dbContextFactory;
    private readonly IDaemonAssignmentPersistence persistence;

    public PostgreSqlDaemonAssignmentCompliance(PostgreSqlContainerFixture containerFixture)
    {
        adminConnectionString = containerFixture.ConnectionString;
        databaseName = $"emit_da_{Guid.NewGuid():N}"[..30];

        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = databaseName,
            MaxPoolSize = 5,
            MinPoolSize = 0
        };
        testConnectionString = builder.ConnectionString;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<IntegrationTestDbContext>(dbOptions =>
        {
            dbOptions.UseNpgsql(testConnectionString);
        });
        services.AddEmit(emit =>
        {
            emit.AddEntityFrameworkCore<IntegrationTestDbContext>(ef =>
            {
                ef.UseNpgsql();
            });
        });

        serviceProvider = services.BuildServiceProvider();
        dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<IntegrationTestDbContext>>();
        persistence = serviceProvider.GetRequiredService<IDaemonAssignmentPersistence>();
    }

    /// <inheritdoc/>
    protected override IDaemonAssignmentPersistence Persistence => persistence;

    /// <inheritdoc/>
    public override async Task InitializeAsync()
    {
        // Create the test database
        await using var adminConnection = new NpgsqlConnection(adminConnectionString);
        await adminConnection.OpenAsync();

        await using var createDbCmd = adminConnection.CreateCommand();
        createDbCmd.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await createDbCmd.ExecuteNonQueryAsync();

        // Ensure schema is created (includes daemon_assignments table from AddEmitModel)
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await dbContext.Database.EnsureCreatedAsync();
    }

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        serviceProvider.Dispose();

        NpgsqlConnection.ClearPool(new NpgsqlConnection(testConnectionString));

        await using var adminConnection = new NpgsqlConnection(adminConnectionString);
        await adminConnection.OpenAsync();

        await using var terminateCmd = adminConnection.CreateCommand();
        terminateCmd.CommandText = $"""
            SELECT pg_terminate_backend(pg_stat_activity.pid)
            FROM pg_stat_activity
            WHERE pg_stat_activity.datname = '{databaseName}'
            AND pid <> pg_backend_pid()
            """;
        await terminateCmd.ExecuteNonQueryAsync();

        await using var dropDbCmd = adminConnection.CreateCommand();
        dropDbCmd.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\"";
        await dropDbCmd.ExecuteNonQueryAsync();
    }
}

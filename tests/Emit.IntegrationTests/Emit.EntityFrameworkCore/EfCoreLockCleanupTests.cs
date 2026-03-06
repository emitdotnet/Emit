namespace Emit.EntityFrameworkCore.Tests.LockCleanup;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.EntityFrameworkCore.DependencyInjection;
using Emit.EntityFrameworkCore.Tests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

/// <summary>
/// Integration tests for the EF Core lock cleanup worker. Verifies that expired lock rows
/// are removed from the database while active (non-expired) lock rows are preserved.
/// </summary>
[Trait("Category", "Integration")]
public class EfCoreLockCleanupTests : IClassFixture<PostgreSqlContainerFixture>, IAsyncLifetime
{
    private readonly string adminConnectionString;
    private readonly string databaseName;
    private readonly string testConnectionString;
    private readonly ServiceProvider serviceProvider;
    private readonly IDbContextFactory<IntegrationTestDbContext> dbContextFactory;
    private readonly IDistributedLockProvider lockProvider;

    public EfCoreLockCleanupTests(PostgreSqlContainerFixture containerFixture)
    {
        adminConnectionString = containerFixture.ConnectionString;
        databaseName = $"emit_cleanup_{Guid.NewGuid():N}"[..30];

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
        services.AddEmit(builder =>
        {
            builder.AddEntityFrameworkCore<IntegrationTestDbContext>(ef =>
            {
                ef.UseNpgsql();
                ef.UseDistributedLock();
            });
        });

        serviceProvider = services.BuildServiceProvider();
        dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<IntegrationTestDbContext>>();
        lockProvider = serviceProvider.GetRequiredService<IDistributedLockProvider>();
    }

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        await using var adminConnection = new NpgsqlConnection(adminConnectionString);
        await adminConnection.OpenAsync();

        await using var createDbCmd = adminConnection.CreateCommand();
        createDbCmd.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await createDbCmd.ExecuteNonQueryAsync();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await dbContext.Database.EnsureCreatedAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
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

    /// <summary>
    /// Verifies that when the cleanup SQL runs, expired lock rows are deleted while
    /// active (non-expired) lock rows remain intact.
    /// </summary>
    [Fact]
    public async Task GivenExpiredLockRows_WhenCleanupSqlExecuted_ThenExpiredLocksRemovedActiveLocksPreserved()
    {
        // Arrange — insert one expired row and one active row directly via SQL.
        await using var connection = new NpgsqlConnection(testConnectionString);
        await connection.OpenAsync();

        var expiredKey = $"expired-lock-{Guid.NewGuid():N}";
        var activeKey = $"active-lock-{Guid.NewGuid():N}";

        await using (var insertCmd = connection.CreateCommand())
        {
            insertCmd.CommandText = $"""
                INSERT INTO "emit_locks" (key, lock_id, expires_at)
                VALUES (@expiredKey, @expiredLockId, @expiredAt),
                       (@activeKey, @activeLockId, @activeAt)
                """;
            insertCmd.Parameters.AddWithValue("expiredKey", expiredKey);
            insertCmd.Parameters.AddWithValue("expiredLockId", Guid.NewGuid());
            insertCmd.Parameters.AddWithValue("expiredAt", DateTime.UtcNow.AddMinutes(-10));
            insertCmd.Parameters.AddWithValue("activeKey", activeKey);
            insertCmd.Parameters.AddWithValue("activeLockId", Guid.NewGuid());
            insertCmd.Parameters.AddWithValue("activeAt", DateTime.UtcNow.AddMinutes(10));
            await insertCmd.ExecuteNonQueryAsync();
        }

        // Act — run the same cleanup SQL that LockCleanupWorker uses.
        await using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.CommandText = """DELETE FROM "emit_locks" WHERE expires_at < clock_timestamp()""";
            var deleted = await deleteCmd.ExecuteNonQueryAsync();
            Assert.Equal(1, deleted);
        }

        // Assert — expired row is gone; active row remains.
        await using var countCmd = connection.CreateCommand();
        countCmd.CommandText = $"""
            SELECT COUNT(*) FROM "emit_locks" WHERE key = @expiredKey
            """;
        countCmd.Parameters.AddWithValue("expiredKey", expiredKey);
        var expiredCount = (long)(await countCmd.ExecuteScalarAsync())!;
        Assert.Equal(0, expiredCount);

        await using var activeCountCmd = connection.CreateCommand();
        activeCountCmd.CommandText = $"""
            SELECT COUNT(*) FROM "emit_locks" WHERE key = @activeKey
            """;
        activeCountCmd.Parameters.AddWithValue("activeKey", activeKey);
        var activeCount = (long)(await activeCountCmd.ExecuteScalarAsync())!;
        Assert.Equal(1, activeCount);
    }
}

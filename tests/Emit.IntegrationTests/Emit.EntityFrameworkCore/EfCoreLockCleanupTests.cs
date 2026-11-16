namespace Emit.EntityFrameworkCore.Tests.LockCleanup;

using Emit.Abstractions;
using Emit.Abstractions.Daemon;
using Emit.DependencyInjection;
using Emit.EntityFrameworkCore.Configuration;
using Emit.EntityFrameworkCore.Daemon;
using Emit.EntityFrameworkCore.DependencyInjection;
using Emit.EntityFrameworkCore.Tests.TestInfrastructure;
using Emit.IntegrationTests.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

/// <summary>
/// Integration tests for the EF Core lock cleanup daemon. Verifies that expired lock rows
/// are removed from the database while active (non-expired) lock rows are preserved.
/// </summary>
[Trait("Category", "Integration")]
public class EfCoreLockCleanupTests : IClassFixture<PostgreSqlContainerFixture>, IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture containerFixture;
    private PostgreSqlTestDatabase testDb = null!;
    private ServiceProvider serviceProvider = null!;
    private IDbContextFactory<IntegrationTestDbContext> dbContextFactory = null!;
    private IDistributedLockProvider lockProvider = null!;

    public EfCoreLockCleanupTests(PostgreSqlContainerFixture containerFixture)
    {
        this.containerFixture = containerFixture;
    }

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        testDb = await PostgreSqlTestDatabase.CreateAsync(containerFixture.ConnectionString, "lockclean");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<IntegrationTestDbContext>(dbOptions =>
        {
            dbOptions.UseNpgsql(testDb.ConnectionString);
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

        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await dbContext.Database.EnsureCreatedAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        serviceProvider.Dispose();
        await testDb.DropAsync();
    }

    /// <summary>
    /// Verifies that the lock cleanup daemon deletes expired lock rows while active (non-expired) lock rows remain
    /// intact.
    /// </summary>
    [Fact]
    public async Task GivenExpiredLockRows_WhenTheCleanupDaemonRuns_ThenExpiredLocksRemovedActiveLocksPreserved()
    {
        // Arrange — insert one expired row and one active row directly via SQL.
        await using var connection = new NpgsqlConnection(testDb.ConnectionString);
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

        // Act: the daemon runs until the expired row is gone
        var daemon = new LockCleanupDaemon<IntegrationTestDbContext>(
            dbContextFactory,
            Options.Create(new LockCleanupOptions { CleanupInterval = TimeSpan.FromMilliseconds(100) }),
            TimeProvider.System,
            NullLogger<LockCleanupDaemon<IntegrationTestDbContext>>.Instance);
        using var assignment = new CancellationTokenSource();
        var run = daemon.RunAsync(new DaemonRunContext(assignment.Token, 1, Guid.NewGuid(), () => true));

        try
        {
            await TestHelpers.WaitUntilAsync(
                async () =>
                {
                    await using var countCmd = connection.CreateCommand();
                    countCmd.CommandText = """SELECT COUNT(*) FROM "emit_locks" WHERE key = @expiredKey""";
                    countCmd.Parameters.AddWithValue("expiredKey", expiredKey);
                    return (long)(await countCmd.ExecuteScalarAsync())! == 0;
                },
                "The daemon never deleted the expired lock row.");
        }
        finally
        {
            await assignment.CancelAsync();
            await run;
        }

        // Assert: the active row remains
        await using var activeCountCmd = connection.CreateCommand();
        activeCountCmd.CommandText = $"""
            SELECT COUNT(*) FROM "emit_locks" WHERE key = @activeKey
            """;
        activeCountCmd.Parameters.AddWithValue("activeKey", activeKey);
        var activeCount = (long)(await activeCountCmd.ExecuteScalarAsync())!;
        Assert.Equal(1, activeCount);
    }
}

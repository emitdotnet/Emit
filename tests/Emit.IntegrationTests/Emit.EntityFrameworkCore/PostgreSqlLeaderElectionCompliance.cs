namespace Emit.EntityFrameworkCore.Tests.LeaderElection;

using Emit.Abstractions.LeaderElection;
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
/// PostgreSQL integration tests for <see cref="EfCoreLeaderElectionPersistence{TDbContext}"/>.
/// </summary>
[Trait("Category", "Integration")]
public class PostgreSqlLeaderElectionCompliance : DatabaseLeaderElectionCompliance, IClassFixture<PostgreSqlContainerFixture>
{
    private readonly PostgreSqlContainerFixture containerFixture;
    private PostgreSqlTestDatabase testDb = null!;
    private ServiceProvider serviceProvider = null!;
    private ILeaderElectionPersistence persistence = null!;

    public PostgreSqlLeaderElectionCompliance(PostgreSqlContainerFixture containerFixture)
    {
        this.containerFixture = containerFixture;
    }

    /// <inheritdoc/>
    protected override ILeaderElectionPersistence Persistence => persistence;

    /// <inheritdoc/>
    protected override async Task<IAsyncDisposable> BlockLeaseAsync(string serviceName)
    {
        var connection = new NpgsqlConnection(testDb.ConnectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """SELECT 1 FROM "emit_leader" WHERE service_name = @serviceName FOR UPDATE""";
        command.Parameters.AddWithValue("serviceName", serviceName);
        await command.ExecuteNonQueryAsync();
        return new LeaseBlock(connection, transaction);
    }

    /// <inheritdoc/>
    public override async Task InitializeAsync()
    {
        testDb = await PostgreSqlTestDatabase.CreateAsync(containerFixture.ConnectionString, "leader");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<IntegrationTestDbContext>(dbOptions =>
        {
            dbOptions.UseNpgsql(testDb.ConnectionString);
        });
        services.AddEmit(emit =>
        {
            emit.AddEntityFrameworkCore<IntegrationTestDbContext>(ef =>
            {
                ef.UseNpgsql();
            });
        });

        serviceProvider = services.BuildServiceProvider();
        persistence = serviceProvider.GetRequiredService<ILeaderElectionPersistence>();
    }

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        serviceProvider.Dispose();
        await testDb.DropAsync();
    }

    private sealed class LeaseBlock(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await transaction.RollbackAsync();
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}

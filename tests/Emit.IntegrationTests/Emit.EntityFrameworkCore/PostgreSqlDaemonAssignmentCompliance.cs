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
    private readonly PostgreSqlContainerFixture containerFixture;
    private PostgreSqlTestDatabase testDb = null!;
    private ServiceProvider serviceProvider = null!;
    private IDaemonAssignmentPersistence persistence = null!;

    public PostgreSqlDaemonAssignmentCompliance(PostgreSqlContainerFixture containerFixture)
    {
        this.containerFixture = containerFixture;
    }

    /// <inheritdoc/>
    protected override IDaemonAssignmentPersistence Persistence => persistence;

    /// <inheritdoc/>
    public override async Task InitializeAsync()
    {
        testDb = await PostgreSqlTestDatabase.CreateAsync(containerFixture.ConnectionString, "daemon");

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
        persistence = serviceProvider.GetRequiredService<IDaemonAssignmentPersistence>();
    }

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        serviceProvider.Dispose();
        await testDb.DropAsync();
    }
}

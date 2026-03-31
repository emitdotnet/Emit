namespace Emit.EntityFrameworkCore.Tests.LeaderElection;

using Emit.Abstractions.LeaderElection;
using Emit.DependencyInjection;
using Emit.EntityFrameworkCore;
using Emit.EntityFrameworkCore.DependencyInjection;
using Emit.EntityFrameworkCore.Tests.TestInfrastructure;
using Emit.IntegrationTests.Integration.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// PostgreSQL integration tests for <see cref="EfCoreLeaderElectionPersistence{TDbContext}"/>.
/// </summary>
[Trait("Category", "Integration")]
public class PostgreSqlLeaderElectionCompliance : LeaderElectionCompliance, IClassFixture<PostgreSqlContainerFixture>
{
    private readonly PostgreSqlContainerFixture containerFixture;
    private PostgreSqlTestDatabase testDb = null!;
    private ServiceProvider serviceProvider = null!;
    private IDbContextFactory<IntegrationTestDbContext> dbContextFactory = null!;
    private ILeaderElectionPersistence persistence = null!;

    public PostgreSqlLeaderElectionCompliance(PostgreSqlContainerFixture containerFixture)
    {
        this.containerFixture = containerFixture;
    }

    /// <inheritdoc/>
    protected override ILeaderElectionPersistence Persistence => persistence;

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
        dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<IntegrationTestDbContext>>();
        persistence = serviceProvider.GetRequiredService<ILeaderElectionPersistence>();

        // Ensure schema is created (includes leader and nodes tables from AddEmitModel)
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await dbContext.Database.EnsureCreatedAsync();
    }

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        serviceProvider.Dispose();
        await testDb.DropAsync();
    }
}

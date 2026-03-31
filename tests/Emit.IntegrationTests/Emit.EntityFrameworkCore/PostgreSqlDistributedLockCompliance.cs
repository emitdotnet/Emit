namespace Emit.EntityFrameworkCore.Tests.DistributedLock;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.EntityFrameworkCore;
using Emit.EntityFrameworkCore.DependencyInjection;
using Emit.EntityFrameworkCore.Tests.TestInfrastructure;
using Emit.IntegrationTests.Integration.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// PostgreSQL integration tests for <see cref="EfCoreDistributedLockProvider{TDbContext}"/>.
/// </summary>
[Trait("Category", "Integration")]
public class PostgreSqlDistributedLockCompliance : DistributedLockCompliance, IClassFixture<PostgreSqlContainerFixture>
{
    private readonly PostgreSqlContainerFixture containerFixture;
    private PostgreSqlTestDatabase testDb = null!;
    private ServiceProvider serviceProvider = null!;
    private IDbContextFactory<IntegrationTestDbContext> dbContextFactory = null!;
    private IDistributedLockProvider lockProvider = null!;

    public PostgreSqlDistributedLockCompliance(PostgreSqlContainerFixture containerFixture)
    {
        this.containerFixture = containerFixture;
    }

    /// <inheritdoc/>
    protected override IDistributedLockProvider LockProvider => lockProvider;

    /// <inheritdoc/>
    public override async Task InitializeAsync()
    {
        testDb = await PostgreSqlTestDatabase.CreateAsync(containerFixture.ConnectionString, "distlock");

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

        // Ensure schema is created (includes locks table from AddEmitModel)
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

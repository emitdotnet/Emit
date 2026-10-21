namespace Emit.EntityFrameworkCore.Tests.LeaderElection;

using Emit.DependencyInjection;
using Emit.EntityFrameworkCore.DependencyInjection;
using Emit.EntityFrameworkCore.Tests.TestInfrastructure;
using Emit.IntegrationTests.Integration.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// PostgreSQL implementation of <see cref="ServiceCoordinationCompliance"/>.
/// </summary>
[Trait("Category", "Integration")]
public class PostgreSqlServiceCoordinationCompliance(PostgreSqlContainerFixture postgresFixture)
    : ServiceCoordinationCompliance, IClassFixture<PostgreSqlContainerFixture>
{
    private PostgreSqlTestDatabase testDb = null!;

    /// <inheritdoc/>
    public override async Task InitializeAsync()
    {
        testDb = await PostgreSqlTestDatabase.CreateAsync(postgresFixture.ConnectionString, "svc");
    }

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        await testDb.DropAsync();
    }

    /// <inheritdoc/>
    protected override void ConfigurePersistence(EmitBuilder emit)
    {
        emit.Services.AddDbContextFactory<IntegrationTestDbContext>(opts =>
            opts.UseNpgsql(testDb.ConnectionString));

        emit.AddEntityFrameworkCore<IntegrationTestDbContext>(ef => ef.UseNpgsql());
    }
}

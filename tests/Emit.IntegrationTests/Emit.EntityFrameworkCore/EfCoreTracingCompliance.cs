namespace Emit.EntityFrameworkCore.Tests.Outbox;

using Emit.Configuration;
using Emit.DependencyInjection;
using Emit.EntityFrameworkCore.DependencyInjection;
using Emit.EntityFrameworkCore.Tests.TestInfrastructure;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.Tests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// EF Core (PostgreSQL) + Kafka implementation of <see cref="TracingCompliance"/>.
/// </summary>
[Trait("Category", "Integration")]
public class EfCoreTracingCompliance(
    PostgreSqlContainerFixture postgresFixture,
    KafkaContainerFixture kafkaFixture)
    : TracingCompliance,
      IClassFixture<PostgreSqlContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private PostgreSqlTestDatabase testDb = null!;

    /// <inheritdoc/>
    protected override string BootstrapServers => kafkaFixture.BootstrapServers;

    /// <inheritdoc/>
    public override async Task InitializeAsync()
    {
        testDb = await PostgreSqlTestDatabase.CreateAsync(postgresFixture.ConnectionString, "tracing");
    }

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        await testDb.DropAsync();
    }

    /// <inheritdoc/>
    protected override void ConfigurePersistence(EmitBuilder emit, Action<OutboxOptions> configureOutbox)
    {
        // IDbContextFactory + TDbContext (scoped) must be registered before AddEntityFrameworkCore.
        emit.Services.AddDbContextFactory<IntegrationTestDbContext>(opts =>
            opts.UseNpgsql(testDb.ConnectionString));

        emit.AddEntityFrameworkCore<IntegrationTestDbContext>(ef =>
        {
            ef.UseNpgsql();
            ef.UseOutbox(configureOutbox);
        });
    }

    /// <inheritdoc/>
    protected override async Task FlushBeforeCommitAsync(IServiceProvider scopedServices)
    {
        var dbContext = scopedServices.GetRequiredService<IntegrationTestDbContext>();
        await dbContext.SaveChangesAsync();
    }
}

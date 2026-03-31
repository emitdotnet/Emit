namespace Emit.EntityFrameworkCore.Tests.Outbox;

using Emit.DependencyInjection;
using Emit.EntityFrameworkCore.DependencyInjection;
using Emit.EntityFrameworkCore.Tests.TestInfrastructure;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.Tests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// EF Core (PostgreSQL) + Kafka implementation of <see cref="TransactionalMiddlewareCompliance"/>.
/// Verifies that the [Transactional] middleware integrates correctly with the EF Core outbox.
/// </summary>
[Trait("Category", "Integration")]
public class EfCoreTransactionalMiddlewareCompliance(
    PostgreSqlContainerFixture postgresFixture,
    KafkaContainerFixture kafkaFixture)
    : TransactionalMiddlewareCompliance,
      IClassFixture<PostgreSqlContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private PostgreSqlTestDatabase testDb = null!;

    protected override string BootstrapServers => kafkaFixture.BootstrapServers;

    /// <inheritdoc/>
    public override async Task InitializeAsync()
    {
        await postgresFixture.InitializeAsync();
        await kafkaFixture.InitializeAsync();
        testDb = await PostgreSqlTestDatabase.CreateAsync(postgresFixture.ConnectionString, "txnmw");
    }

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        await testDb.DropAsync();
    }

    /// <inheritdoc/>
    protected override void ConfigurePersistence(EmitBuilder emit, TimeSpan pollingInterval)
    {
        emit.Services.AddDbContextFactory<IntegrationTestDbContext>(opts =>
            opts.UseNpgsql(testDb.ConnectionString));

        emit.AddEntityFrameworkCore<IntegrationTestDbContext>(ef =>
        {
            ef.UseNpgsql();
            ef.UseOutbox(opts => opts.PollingInterval = pollingInterval);
        });
    }
}

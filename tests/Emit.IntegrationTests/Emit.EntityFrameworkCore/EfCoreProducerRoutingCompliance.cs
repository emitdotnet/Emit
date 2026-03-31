namespace Emit.EntityFrameworkCore.Tests.Outbox;

using Emit.DependencyInjection;
using Emit.EntityFrameworkCore.DependencyInjection;
using Emit.EntityFrameworkCore.Tests.TestInfrastructure;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.Tests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <inheritdoc />
[Trait("Category", "Integration")]
public class EfCoreProducerRoutingCompliance(
    PostgreSqlContainerFixture postgresFixture,
    KafkaContainerFixture kafkaFixture)
    : ProducerRoutingCompliance,
      IClassFixture<PostgreSqlContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private PostgreSqlTestDatabase testDb = null!;

    /// <inheritdoc />
    protected override string BootstrapServers => kafkaFixture.BootstrapServers;

    /// <inheritdoc />
    public override async Task InitializeAsync()
    {
        await postgresFixture.InitializeAsync();
        await kafkaFixture.InitializeAsync();
        testDb = await PostgreSqlTestDatabase.CreateAsync(postgresFixture.ConnectionString, "prodroute");
    }

    /// <inheritdoc />
    public override async Task DisposeAsync()
    {
        await testDb.DropAsync();
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    protected override async Task FlushBeforeCommitAsync(IServiceProvider scopedServices)
    {
        var dbContext = scopedServices.GetRequiredService<IntegrationTestDbContext>();
        await dbContext.SaveChangesAsync();
    }
}

namespace Emit.EntityFrameworkCore.Tests.Outbox;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.EntityFrameworkCore.DependencyInjection;
using Emit.EntityFrameworkCore.Tests.TestInfrastructure;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// EF Core (PostgreSQL) + Kafka implementation of <see cref="OutboxDeliveryCompliance"/>.
/// Verifies that the EF Core transactional outbox delivers messages via Kafka.
/// </summary>
[Trait("Category", "Integration")]
public class EfCoreKafkaOutboxCompliance(
    PostgreSqlContainerFixture postgresFixture,
    KafkaContainerFixture kafkaFixture)
    : OutboxDeliveryCompliance,
      IClassFixture<PostgreSqlContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private PostgreSqlTestDatabase testDb = null!;

    /// <inheritdoc/>
    protected override string BootstrapServers => kafkaFixture.BootstrapServers;

    /// <inheritdoc/>
    public override async Task InitializeAsync()
    {
        await postgresFixture.InitializeAsync();
        await kafkaFixture.InitializeAsync();
        testDb = await PostgreSqlTestDatabase.CreateAsync(postgresFixture.ConnectionString, "outbox");
    }

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        await testDb.DropAsync();
    }

    /// <inheritdoc/>
    protected override void ConfigurePersistence(EmitBuilder emit, TimeSpan pollingInterval)
    {
        // IDbContextFactory + TDbContext (scoped) must be registered before AddEntityFrameworkCore.
        emit.Services.AddDbContextFactory<IntegrationTestDbContext>(opts =>
            opts.UseNpgsql(testDb.ConnectionString));

        emit.AddEntityFrameworkCore<IntegrationTestDbContext>(ef =>
        {
            ef.UseNpgsql();
            ef.UseOutbox(opts => opts.PollingInterval = pollingInterval);
        });
    }

    /// <inheritdoc/>
    protected override async Task ProduceTransactionallyAsync(
        IServiceProvider services,
        string key,
        string value,
        bool commit,
        CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
        var dbContext = sp.GetRequiredService<IntegrationTestDbContext>();
        var producer = sp.GetRequiredService<IEventProducer<string, string>>();

        await using var transaction = await unitOfWork.BeginAsync(ct)
            .ConfigureAwait(false);

        await producer.ProduceAsync(new EventMessage<string, string>(key, value), ct)
            .ConfigureAwait(false);

        if (commit)
        {
            // Flush the EF Core change tracker (including the outbox entry) before committing.
            await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        else
        {
            // Roll back without saving — the outbox entry remains only in the change tracker.
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    protected override async Task EnqueueDirectlyAsync(
        IServiceProvider services,
        OutboxEntry entry,
        CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var dbContext = sp.GetRequiredService<IntegrationTestDbContext>();
        var repository = sp.GetRequiredService<IOutboxRepository>();

        await repository.EnqueueAsync(entry, ct).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

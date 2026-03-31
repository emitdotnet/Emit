namespace Emit.EntityFrameworkCore.Tests.Outbox;

using Emit.Abstractions;
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
public class EfCoreImplicitOutboxCompliance(
    PostgreSqlContainerFixture postgresFixture,
    KafkaContainerFixture kafkaFixture)
    : ImplicitOutboxCompliance,
      IClassFixture<PostgreSqlContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private PostgreSqlTestDatabase testDb = null!;

    protected override string BootstrapServers => kafkaFixture.BootstrapServers;

    /// <inheritdoc />
    public override async Task InitializeAsync()
    {
        await postgresFixture.InitializeAsync();
        await kafkaFixture.InitializeAsync();
        testDb = await PostgreSqlTestDatabase.CreateAsync(postgresFixture.ConnectionString, "implicit");
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
    protected override async Task ProduceAndSaveAsync(
        IServiceProvider services,
        string key,
        string value,
        bool save,
        CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var producer = sp.GetRequiredService<IEventProducer<string, string>>();
        var dbContext = sp.GetRequiredService<IntegrationTestDbContext>();

        await producer.ProduceAsync(new EventMessage<string, string>(key, value), ct);

        if (save)
        {
            await dbContext.SaveChangesAsync(ct);
        }
    }

    /// <inheritdoc />
    protected override async Task ProduceTwoWithInterleavedSaveAsync(
        IServiceProvider services,
        string key,
        string value1,
        string value2,
        CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var producer = sp.GetRequiredService<IEventProducer<string, string>>();
        var dbContext = sp.GetRequiredService<IntegrationTestDbContext>();

        await producer.ProduceAsync(new EventMessage<string, string>(key, value1), ct);
        await dbContext.SaveChangesAsync(ct);

        await producer.ProduceAsync(new EventMessage<string, string>(key, value2), ct);
        await dbContext.SaveChangesAsync(ct);
    }
}

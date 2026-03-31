namespace Emit.MongoDB.Tests.Outbox;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Models;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Tests.TestInfrastructure;
using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// MongoDB + Kafka implementation of <see cref="OutboxDeliveryCompliance"/>.
/// Verifies that the MongoDB transactional outbox delivers messages via Kafka.
/// </summary>
[Trait("Category", "Integration")]
public class MongoDbKafkaOutboxCompliance(
    MongoDbContainerFixture mongoFixture,
    KafkaContainerFixture kafkaFixture)
    : OutboxDeliveryCompliance,
      IClassFixture<MongoDbContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private readonly string databaseName = $"emit_outbox_{Guid.NewGuid():N}";
    private readonly IMongoClient mongoClient = new MongoClient(mongoFixture.ConnectionString);

    /// <inheritdoc/>
    protected override string BootstrapServers => kafkaFixture.BootstrapServers;

    /// <inheritdoc/>
    public override async Task InitializeAsync()
    {
        await mongoFixture.InitializeAsync();
        await kafkaFixture.InitializeAsync();
    }

    /// <inheritdoc/>
    public override async Task DisposeAsync()
    {
        await mongoClient.DropDatabaseAsync(databaseName);
    }

    /// <inheritdoc/>
    protected override void ConfigurePersistence(EmitBuilder emit, TimeSpan pollingInterval)
    {
        emit.AddMongoDb(mongo =>
        {
            mongo.Configure((_, ctx) =>
            {
                ctx.Client = mongoClient;
                ctx.Database = mongoClient.GetDatabase(databaseName);
            });
            mongo.UseOutbox(opts => opts.PollingInterval = pollingInterval);
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
        var producer = sp.GetRequiredService<IEventProducer<string, string>>();

        await using var transaction = await unitOfWork.BeginAsync(ct)
            .ConfigureAwait(false);

        await producer.ProduceAsync(new EventMessage<string, string>(key, value), ct)
            .ConfigureAwait(false);

        if (commit)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        else
        {
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
        var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
        var repository = sp.GetRequiredService<IOutboxRepository>();

        // MongoDB EnqueueAsync requires an active transaction.
        await using var transaction = await unitOfWork.BeginAsync(ct)
            .ConfigureAwait(false);

        await repository.EnqueueAsync(entry, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }
}

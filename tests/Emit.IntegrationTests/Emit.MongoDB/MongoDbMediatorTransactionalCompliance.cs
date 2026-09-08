namespace Emit.MongoDB.Tests.Outbox;

using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.MongoDB;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Tests.TestInfrastructure;
using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// MongoDB implementation of <see cref="MediatorTransactionalCompliance"/>.
/// Adds facts asserting <see cref="IMongoSessionAccessor.Session"/>, the handle applications
/// pass to driver calls, which is the symptom users observe when [Transactional] is ignored.
/// </summary>
[Trait("Category", "Integration")]
public class MongoDbMediatorTransactionalCompliance
    : MediatorTransactionalCompliance,
      IClassFixture<MongoDbContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private readonly string databaseName;
    private readonly IMongoClient mongoClient;
    private readonly MongoDbContainerFixture mongoFixture;
    private readonly KafkaContainerFixture kafkaFixture;

    public MongoDbMediatorTransactionalCompliance(
        MongoDbContainerFixture mongoFixture,
        KafkaContainerFixture kafkaFixture)
    {
        this.mongoFixture = mongoFixture;
        this.kafkaFixture = kafkaFixture;
        databaseName = $"emit_medtxn_{Guid.NewGuid():N}";
        mongoClient = new MongoClient(mongoFixture.ConnectionString);
    }

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
    protected override void ConfigurePersistence(EmitBuilder emit)
    {
        emit.AddMongoDb(mongo =>
        {
            mongo.Configure((_, ctx) =>
            {
                ctx.Client = mongoClient;
                ctx.Database = mongoClient.GetDatabase(databaseName);
            });
            mongo.UseOutbox();
        });
    }

    /// <inheritdoc/>
    protected override void ConfigurePersistenceWithoutOutbox(EmitBuilder emit)
    {
        emit.AddMongoDb(mongo => mongo.Configure((_, ctx) =>
        {
            ctx.Client = mongoClient;
            ctx.Database = mongoClient.GetDatabase(databaseName);
        }));
    }

    /// <inheritdoc/>
    protected override void RegisterAmbientSessionInspector(IServiceCollection services)
        => services.AddScoped<IAmbientSessionInspector, MongoAmbientSessionInspector>();

    // ── MongoDB-specific: the session handle applications actually use ──

    [Fact]
    public async Task GivenTransactionalResponseHandler_WhenSent_ThenMongoSessionAvailableInsideHandler()
    {
        // Arrange
        var (host, probe) = BuildHost(m => m.AddHandler<TransactionalResponseHandler>());

        try
        {
            // Act
            await SendAsync(host, new TransactionalResponseRequest("hello"));

            // Assert
            var observation = Assert.Single(probe.Observations);
            Assert.NotNull(observation.ProviderSession);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GivenTransactionalVoidHandler_WhenSent_ThenMongoSessionAvailableInsideHandler()
    {
        // Arrange
        var (host, probe) = BuildHost(m => m.AddHandler<TransactionalVoidHandler>());

        try
        {
            // Act
            await SendAsync(host, new TransactionalVoidRequest());

            // Assert
            var observation = Assert.Single(probe.Observations);
            Assert.NotNull(observation.ProviderSession);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GivenPlainResponseHandler_WhenSent_ThenNoMongoSessionInsideHandler()
    {
        // Arrange
        var (host, probe) = BuildHost(m => m.AddHandler<PlainResponseHandler>());

        try
        {
            // Act
            await SendAsync(host, new PlainResponseRequest("hello"));

            // Assert
            var observation = Assert.Single(probe.Observations);
            Assert.Null(observation.ProviderSession);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Exposes the MongoDB session handle to the provider-agnostic compliance facts.
    /// </summary>
    private sealed class MongoAmbientSessionInspector(IMongoSessionAccessor accessor) : IAmbientSessionInspector
    {
        public object? CurrentSession => accessor.Session;
    }
}

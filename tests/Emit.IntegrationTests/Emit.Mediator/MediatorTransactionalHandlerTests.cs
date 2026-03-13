namespace Emit.Mediator.Tests;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.EntityFrameworkCore.DependencyInjection;
using Emit.EntityFrameworkCore.Tests;
using Emit.EntityFrameworkCore.Tests.TestInfrastructure;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Mediator.DependencyInjection;
using Emit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

[Trait("Category", "Integration")]
public sealed class MediatorTransactionalHandlerTests
    : IClassFixture<PostgreSqlContainerFixture>,
      IClassFixture<KafkaContainerFixture>,
      IAsyncLifetime
{
    private readonly string databaseName;
    private readonly string testConnectionString;
    private readonly PostgreSqlContainerFixture postgresFixture;
    private readonly KafkaContainerFixture kafkaFixture;

    public MediatorTransactionalHandlerTests(
        PostgreSqlContainerFixture postgresFixture,
        KafkaContainerFixture kafkaFixture)
    {
        this.postgresFixture = postgresFixture;
        this.kafkaFixture = kafkaFixture;
        databaseName = $"emit_medtxn_{Guid.NewGuid():N}"[..30];

        var builder = new NpgsqlConnectionStringBuilder(postgresFixture.ConnectionString)
        {
            Database = databaseName,
            MaxPoolSize = 5,
            MinPoolSize = 0
        };
        testConnectionString = builder.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        await postgresFixture.InitializeAsync();
        await kafkaFixture.InitializeAsync();

        await using var adminConnection = new NpgsqlConnection(postgresFixture.ConnectionString);
        await adminConnection.OpenAsync();

        await using var createCmd = adminConnection.CreateCommand();
        createCmd.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await createCmd.ExecuteNonQueryAsync();

        var services = new ServiceCollection();
        services.AddDbContextFactory<IntegrationTestDbContext>(opts => opts.UseNpgsql(testConnectionString));
        await using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDbContextFactory<IntegrationTestDbContext>>();
        await using var dbContext = await factory.CreateDbContextAsync();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(testConnectionString));

        await using var adminConnection = new NpgsqlConnection(postgresFixture.ConnectionString);
        await adminConnection.OpenAsync();

        await using var terminateCmd = adminConnection.CreateCommand();
        terminateCmd.CommandText = $"""
            SELECT pg_terminate_backend(pg_stat_activity.pid)
            FROM pg_stat_activity
            WHERE pg_stat_activity.datname = '{databaseName}'
            AND pid <> pg_backend_pid()
            """;
        await terminateCmd.ExecuteNonQueryAsync();

        await using var dropCmd = adminConnection.CreateCommand();
        dropCmd.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\"";
        await dropCmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task GivenTransactionalMediatorHandler_WhenHandlerProducesToOutbox_ThenDelivered()
    {
        // Arrange
        var topic = $"test-med-txn-commit-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();

        var host = BuildHost(sink, topic, groupId, pollingInterval, h =>
            h.AddHandler<TransactionalProducingHandler>());

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.SendAsync(new ProduceCommand("hello"));

            // Assert — outbox daemon delivers the message.
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("hello", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GivenTransactionalMediatorHandler_WhenHandlerThrows_ThenNotDelivered()
    {
        // Arrange
        var topic = $"test-med-txn-throw-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();

        var host = BuildHost(sink, topic, groupId, pollingInterval, h =>
            h.AddHandler<TransactionalThrowingHandler>());

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => mediator.SendAsync(new ThrowCommand()));

            // Assert — wait for daemon cycles; no delivery.
            await Task.Delay(pollingInterval * 5);
            Assert.Empty(sink.ReceivedMessages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GivenNonTransactionalMediatorHandler_WhenInvoked_ThenNoTransaction()
    {
        // Arrange
        var topic = $"test-med-notxn-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();

        var host = BuildHost(sink, topic, groupId, pollingInterval, h =>
            h.AddHandler<NonTransactionalHandler>());

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.SendAsync(new NoOpCommand());

            // Assert — handler executed successfully (no exception means success).
            // No outbox delivery expected since handler doesn't produce.
            await Task.Delay(pollingInterval * 2);
            Assert.Empty(sink.ReceivedMessages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private IHost BuildHost(
        MessageSink<string> sink,
        string topic,
        string groupId,
        TimeSpan pollingInterval,
        Action<MediatorBuilder> configureMediator)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);

                services.AddDbContextFactory<IntegrationTestDbContext>(opts =>
                    opts.UseNpgsql(testConnectionString));

                services.AddEmit(emit =>
                {
                    emit.AddEntityFrameworkCore<IntegrationTestDbContext>(ef =>
                    {
                        ef.UseNpgsql();
                        ef.UseOutbox(opts => opts.PollingInterval = pollingInterval);
                    });

                    emit.AddKafka(kafka =>
                    {
                        kafka.ConfigureClient(config =>
                        {
                            config.BootstrapServers = kafkaFixture.BootstrapServers;
                        });
                        kafka.AutoProvision();

                        kafka.Topic<string, string>(topic, t =>
                        {
                            t.SetUtf8KeySerializer();
                            t.SetUtf8ValueSerializer();
                            t.SetUtf8KeyDeserializer();
                            t.SetUtf8ValueDeserializer();

                            t.Producer();
                            t.ConsumerGroup(groupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.AddConsumer<SinkConsumer<string>>();
                            });
                        });
                    });

                    emit.AddMediator(configureMediator);
                });
            })
            .Build();
    }

    // ── Request types ──

    private sealed record ProduceCommand(string Value) : IRequest;

    private sealed record ThrowCommand : IRequest;

    private sealed record NoOpCommand : IRequest;

    // ── Handlers ──

    [Transactional]
    private sealed class TransactionalProducingHandler(
        IEventProducer<string, string> producer) : IRequestHandler<ProduceCommand>
    {
        public async Task HandleAsync(ProduceCommand request, CancellationToken cancellationToken)
        {
            await producer.ProduceAsync(
                new EventMessage<string, string>("k", request.Value), cancellationToken)
                ;
        }
    }

    [Transactional]
    private sealed class TransactionalThrowingHandler(
        IEventProducer<string, string> producer) : IRequestHandler<ThrowCommand>
    {
        public async Task HandleAsync(ThrowCommand request, CancellationToken cancellationToken)
        {
            await producer.ProduceAsync(
                new EventMessage<string, string>("k", "will-rollback"), cancellationToken)
                ;
            throw new InvalidOperationException("Simulated handler failure");
        }
    }

    private sealed class NonTransactionalHandler : IRequestHandler<NoOpCommand>
    {
        public Task HandleAsync(NoOpCommand request, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}

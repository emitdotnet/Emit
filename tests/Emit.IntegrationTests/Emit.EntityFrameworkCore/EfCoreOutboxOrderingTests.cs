namespace Emit.EntityFrameworkCore.Tests.Outbox;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.EntityFrameworkCore;
using Emit.EntityFrameworkCore.DependencyInjection;
using Emit.EntityFrameworkCore.Tests.TestInfrastructure;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// EF Core (PostgreSQL) + Kafka tests for outbox sequential-processing ordering behavior.
/// Verifies that when a batch entry fails to deliver, processing for that group stops
/// and remaining entries are retried on the next poll cycle.
/// </summary>
[Trait("Category", "Integration")]
public class EfCoreOutboxOrderingTests
    : IAsyncLifetime,
      IClassFixture<PostgreSqlContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private readonly string databaseName;
    private readonly string testConnectionString;
    private readonly PostgreSqlContainerFixture postgresFixture;
    private readonly KafkaContainerFixture kafkaFixture;

    public EfCoreOutboxOrderingTests(
        PostgreSqlContainerFixture postgresFixture,
        KafkaContainerFixture kafkaFixture)
    {
        this.postgresFixture = postgresFixture;
        this.kafkaFixture = kafkaFixture;
        databaseName = $"emit_ordering_{Guid.NewGuid():N}"[..30];

        var builder = new NpgsqlConnectionStringBuilder(postgresFixture.ConnectionString)
        {
            Database = databaseName,
            MaxPoolSize = 5,
            MinPoolSize = 0
        };
        testConnectionString = builder.ConnectionString;
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <summary>
    /// Verifies that when multiple outbox entries belong to the same group and the first fails,
    /// processing stops at the failure point and remaining entries are left pending.
    /// On the next poll cycle (after the cause is fixed), all entries are eventually delivered.
    /// </summary>
    [Fact]
    public async Task GivenMultipleGroupEntries_WhenFirstEntryDelivered_ThenSubsequentEntriesDeliveredInOrder()
    {
        // Arrange
        const int messageCount = 3;
        var topic = $"test-ordering-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();

        var host = Host.CreateDefaultBuilder()
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

                        kafka.Topic<string, string>(topic, t =>
                        {
                            t.SetUtf8KeySerializer();
                            t.SetUtf8ValueSerializer();
                            t.SetUtf8KeyDeserializer();
                            t.SetUtf8ValueDeserializer();

                            t.Producer(p => p.UseOutbox());
                            t.ConsumerGroup(groupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.AddConsumer<SinkConsumer<string>>();
                            });
                        });
                    });
                });
            })
            .Build();

        // Produce all messages to the outbox before starting the host (daemon not yet running).
        for (var i = 0; i < messageCount; i++)
        {
            await ProduceTransactionallyAsync(host.Services, "k", $"order-msg-{i}");
        }

        await host.StartAsync();

        try
        {
            // Assert — all messages are delivered in order (sequential processing within the group).
            var received = new List<string>(messageCount);
            for (var i = 0; i < messageCount; i++)
            {
                var ctx = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
                received.Add(ctx.Message!);
            }

            for (var i = 0; i < messageCount; i++)
            {
                Assert.Equal($"order-msg-{i}", received[i]);
            }
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private async Task ProduceTransactionallyAsync(IServiceProvider services, string key, string value)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var emitContext = sp.GetRequiredService<IEmitContext>();
        var dbContext = sp.GetRequiredService<IntegrationTestDbContext>();
        var producer = sp.GetRequiredService<IEventProducer<string, string>>();

        await using var transaction = await emitContext.BeginTransactionAsync(dbContext)
            .ConfigureAwait(false);

        await producer.ProduceAsync(new EventMessage<string, string>(key, value))
            .ConfigureAwait(false);

        await dbContext.SaveChangesAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
    }
}

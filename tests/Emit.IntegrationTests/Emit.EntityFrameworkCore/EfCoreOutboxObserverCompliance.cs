namespace Emit.EntityFrameworkCore.Tests.Outbox;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.EntityFrameworkCore.DependencyInjection;
using Emit.EntityFrameworkCore.Tests.TestInfrastructure;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// EF Core (PostgreSQL) + Kafka implementation of <see cref="OutboxObserverCompliance"/>.
/// Verifies that outbox observer callbacks fire when using the EF Core outbox.
/// </summary>
[Trait("Category", "Integration")]
public class EfCoreOutboxObserverCompliance
    : OutboxObserverCompliance,
      IClassFixture<PostgreSqlContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private readonly string databaseName;
    private readonly string testConnectionString;
    private readonly PostgreSqlContainerFixture postgresFixture;
    private readonly KafkaContainerFixture kafkaFixture;

    public EfCoreOutboxObserverCompliance(
        PostgreSqlContainerFixture postgresFixture,
        KafkaContainerFixture kafkaFixture)
    {
        this.postgresFixture = postgresFixture;
        this.kafkaFixture = kafkaFixture;
        databaseName = $"emit_obs_{Guid.NewGuid():N}"[..30];

        var builder = new NpgsqlConnectionStringBuilder(postgresFixture.ConnectionString)
        {
            Database = databaseName,
            MaxPoolSize = 5,
            MinPoolSize = 0
        };
        testConnectionString = builder.ConnectionString;
    }

    /// <inheritdoc/>
    public override async Task InitializeAsync()
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
    public override async Task DisposeAsync()
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

    /// <inheritdoc/>
    protected override void ConfigureEmit(
        EmitBuilder emit,
        string topic,
        string groupId,
        TimeSpan pollingInterval)
    {
        emit.Services.AddDbContextFactory<IntegrationTestDbContext>(opts =>
            opts.UseNpgsql(testConnectionString));

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

                t.Producer(p => p.UseOutbox());
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<SinkConsumer<string>>();
                });
            });
        });
    }

    /// <inheritdoc/>
    protected override async Task ProduceTransactionallyAsync(
        IServiceProvider services,
        string key,
        string value,
        CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var emitContext = sp.GetRequiredService<IEmitContext>();
        var dbContext = sp.GetRequiredService<IntegrationTestDbContext>();
        var producer = sp.GetRequiredService<IEventProducer<string, string>>();

        await using var transaction = await emitContext.BeginTransactionAsync(dbContext, ct)
            .ConfigureAwait(false);

        await producer.ProduceAsync(new EventMessage<string, string>(key, value), ct)
            .ConfigureAwait(false);

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }
}

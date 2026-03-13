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
/// EF Core (PostgreSQL) + Kafka implementation of <see cref="TransactionalMiddlewareCompliance"/>.
/// Verifies that the [Transactional] middleware integrates correctly with the EF Core outbox.
/// </summary>
[Trait("Category", "Integration")]
public class EfCoreTransactionalMiddlewareCompliance
    : TransactionalMiddlewareCompliance,
      IClassFixture<PostgreSqlContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private readonly string databaseName;
    private readonly string testConnectionString;
    private readonly PostgreSqlContainerFixture postgresFixture;
    private readonly KafkaContainerFixture kafkaFixture;

    public EfCoreTransactionalMiddlewareCompliance(
        PostgreSqlContainerFixture postgresFixture,
        KafkaContainerFixture kafkaFixture)
    {
        this.postgresFixture = postgresFixture;
        this.kafkaFixture = kafkaFixture;
        databaseName = $"emit_txnmw_{Guid.NewGuid():N}"[..30];

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
        string inputTopic,
        string outputTopic,
        string groupId,
        TimeSpan pollingInterval,
        Type consumerType)
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

            // Input topic — consumer only; triggered via raw Confluent producer in ProduceDirectAsync.
            kafka.Topic<string, string>(inputTopic, t =>
            {
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.OnError(e => e.Default(d => d.Retry(1, Backoff.None).Discard()));

                    if (consumerType == typeof(TransactionalProducingConsumer))
                        group.AddConsumer<TransactionalProducingConsumer>();
                    else if (consumerType == typeof(TransactionalThrowingConsumer))
                        group.AddConsumer<TransactionalThrowingConsumer>();
                    else if (consumerType == typeof(NonTransactionalSinkConsumer))
                        group.AddConsumer<NonTransactionalSinkConsumer>();
                    else if (consumerType == typeof(TransactionalRetryConsumer))
                        group.AddConsumer<TransactionalRetryConsumer>();
                });
            });

            // Output topic — outbox producer + SinkConsumer.
            kafka.Topic<string, string>(outputTopic, t =>
            {
                t.SetUtf8KeySerializer();
                t.SetUtf8ValueSerializer();
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.Producer();
                t.ConsumerGroup($"{groupId}-out", group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<SinkConsumer<string>>();
                });
            });
        });
    }

    /// <inheritdoc/>
    protected override async Task ProduceDirectAsync(
        IServiceProvider services,
        string topic,
        string key,
        string value,
        CancellationToken ct = default)
    {
        using var producer = new ConfluentKafka.ProducerBuilder<string, string>(
            new ConfluentKafka.ProducerConfig { BootstrapServers = kafkaFixture.BootstrapServers })
            .Build();

        await producer.ProduceAsync(
            topic,
            new ConfluentKafka.Message<string, string> { Key = key, Value = value },
            ct);
    }
}

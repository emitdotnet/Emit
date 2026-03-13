namespace Emit.EntityFrameworkCore.Tests.Outbox;

using System.Text;
using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.EntityFrameworkCore.DependencyInjection;
using Emit.EntityFrameworkCore.Tests.TestInfrastructure;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Models;
using Emit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <inheritdoc />
[Trait("Category", "Integration")]
public class EfCoreProducerRoutingCompliance
    : ProducerRoutingCompliance,
      IClassFixture<PostgreSqlContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private readonly string databaseName;
    private readonly string testConnectionString;
    private readonly PostgreSqlContainerFixture postgresFixture;
    private readonly KafkaContainerFixture kafkaFixture;
    private string? currentOutboxTopic;

    public EfCoreProducerRoutingCompliance(
        PostgreSqlContainerFixture postgresFixture,
        KafkaContainerFixture kafkaFixture)
    {
        this.postgresFixture = postgresFixture;
        this.kafkaFixture = kafkaFixture;
        databaseName = $"emit_route_{Guid.NewGuid():N}"[..30];

        var builder = new NpgsqlConnectionStringBuilder(postgresFixture.ConnectionString)
        {
            Database = databaseName,
            MaxPoolSize = 5,
            MinPoolSize = 0
        };
        testConnectionString = builder.ConnectionString;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    protected override void ConfigureEmit(
        EmitBuilder emit,
        string outboxTopic,
        string directTopic,
        string groupId,
        TimeSpan pollingInterval)
    {
        currentOutboxTopic = outboxTopic;

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

            // Outbox topic — consumer only; entries enqueued directly via IOutboxRepository.
            kafka.Topic<string, string>(outboxTopic, t =>
            {
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.ConsumerGroup($"{groupId}-outbox", group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<OutboxTopicSinkConsumer>();
                });
            });

            // Direct topic — producer with UseDirect() + consumer.
            // IEventProducer<string, string> resolves to this producer (last registration wins).
            kafka.Topic<string, string>(directTopic, t =>
            {
                t.SetUtf8KeySerializer();
                t.SetUtf8ValueSerializer();
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.Producer(p => p.UseDirect());
                t.ConsumerGroup($"{groupId}-direct", group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<DirectTopicSinkConsumer>();
                });
            });
        });
    }

    /// <inheritdoc />
    protected override async Task ProduceViaOutboxAsync(
        IServiceProvider services,
        string key,
        string value,
        CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
        var repository = sp.GetRequiredService<IOutboxRepository>();
        var dbContext = sp.GetRequiredService<IntegrationTestDbContext>();

        await using var tx = await unitOfWork.BeginAsync(ct);

        var keyBytes = Encoding.UTF8.GetBytes(key);
        await repository.EnqueueAsync(new OutboxEntry
        {
            SystemId = "kafka",
            Destination = $"kafka://localhost/{currentOutboxTopic}",
            GroupKey = $"kafka:{currentOutboxTopic}:{Convert.ToBase64String(keyBytes)}",
            Body = Encoding.UTF8.GetBytes(value),
            EnqueuedAt = DateTime.UtcNow,
            Properties = new Dictionary<string, string> { ["key"] = Convert.ToBase64String(keyBytes) },
        }, ct);

        await dbContext.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}

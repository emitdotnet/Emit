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
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// EF Core (PostgreSQL) + Kafka tests for outbox sequential-processing ordering behavior.
/// Verifies that when a batch entry fails to deliver, processing for that group stops
/// and remaining entries are retried on the next poll cycle.
/// </summary>
[Trait("Category", "Integration")]
public class EfCoreOutboxOrderingTests(
    PostgreSqlContainerFixture postgresFixture,
    KafkaContainerFixture kafkaFixture)
    : IAsyncLifetime,
      IClassFixture<PostgreSqlContainerFixture>,
      IClassFixture<KafkaContainerFixture>
{
    private PostgreSqlTestDatabase testDb = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        await postgresFixture.InitializeAsync();
        await kafkaFixture.InitializeAsync();
        testDb = await PostgreSqlTestDatabase.CreateAsync(postgresFixture.ConnectionString, "outboxord");
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        await testDb.DropAsync();
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
                    opts.UseNpgsql(testDb.ConnectionString));
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
                var ctx = await sink.WaitForMessageAsync();
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

        var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
        var dbContext = sp.GetRequiredService<IntegrationTestDbContext>();
        var producer = sp.GetRequiredService<IEventProducer<string, string>>();

        await using var transaction = await unitOfWork.BeginAsync()
            .ConfigureAwait(false);

        await producer.ProduceAsync(new EventMessage<string, string>(key, value))
            .ConfigureAwait(false);

        await dbContext.SaveChangesAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
    }
}

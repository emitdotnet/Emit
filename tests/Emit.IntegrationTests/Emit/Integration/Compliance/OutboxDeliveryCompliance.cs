namespace Emit.IntegrationTests.Integration.Compliance;

using System.Text;
using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Emit.Models;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Compliance tests for the transactional outbox pattern. Covers four scenarios:
/// E2E delivery when a transaction commits, no delivery when a transaction rolls back,
/// ordered delivery of multiple messages, and durability of pending entries across a
/// daemon restart. Derived classes configure a persistence provider (with outbox enabled).
/// Kafka configuration is handled by the base class.
/// </summary>
[Trait("Category", "Integration")]
public abstract class OutboxDeliveryCompliance : IAsyncLifetime
{
    /// <summary>
    /// Gets the Kafka bootstrap servers address for producing and consuming messages.
    /// </summary>
    protected abstract string BootstrapServers { get; }

    /// <summary>
    /// Configures the persistence provider (MongoDB or EF Core) with outbox enabled.
    /// Kafka configuration is handled by the base class.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="pollingInterval">The interval at which the outbox daemon polls for new entries.</param>
    protected abstract void ConfigurePersistence(EmitBuilder emit, TimeSpan pollingInterval);

    /// <summary>
    /// Begins a transaction, produces a message to the outbox, and either commits or rolls back.
    /// Provider-specific implementations resolve <see cref="IUnitOfWork"/> from the scoped service provider
    /// and call <see cref="IUnitOfWork.BeginAsync"/> to start the transaction.
    /// </summary>
    /// <param name="services">The host-level service provider. Creates a new scope internally.</param>
    /// <param name="key">The message key.</param>
    /// <param name="value">The message value.</param>
    /// <param name="commit">
    /// When <see langword="true"/>, commits the transaction; otherwise rolls it back.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    protected abstract Task ProduceTransactionallyAsync(
        IServiceProvider services,
        string key,
        string value,
        bool commit,
        CancellationToken ct = default);

    /// <summary>
    /// Persists a pre-built <see cref="OutboxEntry"/> directly via <see cref="IOutboxRepository"/>
    /// without going through the producer pipeline. Derived classes handle the provider-specific
    /// persistence mechanics (e.g., EF Core change tracker flush, MongoDB transaction).
    /// </summary>
    /// <param name="services">The host-level service provider. Creates a new scope internally.</param>
    /// <param name="entry">The outbox entry to persist.</param>
    /// <param name="ct">A cancellation token.</param>
    protected abstract Task EnqueueDirectlyAsync(
        IServiceProvider services,
        OutboxEntry entry,
        CancellationToken ct = default);

    /// <inheritdoc />
    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Verifies that a committed transactional produce results in delivery to the consumer.
    /// </summary>
    [Fact]
    public async Task GivenCommittedTransaction_WhenOutboxDaemonRuns_ThenMessageDeliveredToConsumer()
    {
        // Arrange
        var topic = $"test-outbox-commit-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();
        var host = BuildHost(sink, topic, groupId, pollingInterval);

        await host.StartAsync();

        try
        {
            // Act — produce and commit; the daemon picks up the outbox entry and delivers to Kafka.
            await ProduceTransactionallyAsync(host.Services, "k", "hello", commit: true);

            // Assert — message arrives at the consumer.
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("hello", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that a rolled-back transactional produce does not result in delivery.
    /// The rolled-back entry must not appear in the outbox; a subsequently committed
    /// sentinel must be the first (and only) message the consumer receives.
    /// </summary>
    [Fact]
    public async Task GivenRolledBackTransaction_WhenOutboxDaemonRuns_ThenNoMessageDelivered()
    {
        // Arrange
        var topic = $"test-outbox-rollback-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();
        var host = BuildHost(sink, topic, groupId, pollingInterval);

        await host.StartAsync();

        try
        {
            // Act — produce and roll back; the outbox entry must not be created.
            await ProduceTransactionallyAsync(host.Services, "k", "will-not-arrive", commit: false);

            // Wait for several daemon polling cycles to confirm no delivery.
            await Task.Delay(pollingInterval * 3);

            // Act — produce a sentinel in a committed transaction.
            await ProduceTransactionallyAsync(host.Services, "k", "sentinel", commit: true);

            // Assert — first message is the sentinel (the rolled-back entry was never delivered).
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("sentinel", ctx.Message);

            // Wait two more cycles and confirm no additional messages arrive.
            await Task.Delay(pollingInterval * 2);
            Assert.Single(sink.ReceivedMessages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that multiple messages produced in separate committed transactions
    /// are delivered to the consumer in the same order they were produced.
    /// </summary>
    [Fact]
    public async Task GivenMultipleCommittedTransactions_WhenOutboxDaemonRuns_ThenMessagesDeliveredInOrder()
    {
        // Arrange
        const int messageCount = 5;
        var topic = $"test-outbox-order-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();
        var host = BuildHost(sink, topic, groupId, pollingInterval);

        await host.StartAsync();

        try
        {
            // Act — produce messages sequentially; each gets a strictly increasing outbox sequence.
            for (var i = 0; i < messageCount; i++)
            {
                await ProduceTransactionallyAsync(host.Services, "k", $"msg-{i}", commit: true);
            }

            // Collect all delivered messages.
            var received = new List<string>(messageCount);
            for (var i = 0; i < messageCount; i++)
            {
                var ctx = await sink.WaitForMessageAsync();
                received.Add(ctx.Message!);
            }

            // Assert — messages arrived in production order.
            for (var i = 0; i < messageCount; i++)
            {
                Assert.Equal($"msg-{i}", received[i]);
            }
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that outbox entries persisted before the daemon starts are delivered
    /// after the daemon starts. This proves that entries survive a daemon restart and
    /// are not lost between an application write and the daemon's first poll cycle.
    /// </summary>
    [Fact]
    public async Task GivenPendingOutboxEntries_WhenDaemonStarts_ThenMessagesDelivered()
    {
        // Arrange — build the host but do not start it; the daemon has not started yet.
        // Services are available from the built (but not started) host.
        var topic = $"test-outbox-durable-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();
        var host = BuildHost(sink, topic, groupId, pollingInterval);

        // Act — produce to the outbox while the daemon is not running.
        await ProduceTransactionallyAsync(host.Services, "k", "durable-msg", commit: true);

        // Start the host — the outbox daemon wakes up and picks up the pending entry.
        await host.StartAsync();

        try
        {
            // Assert — the pending entry is delivered after the daemon starts.
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("durable-msg", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that entries written directly to the outbox repository across multiple group keys
    /// in a deliberately interleaved order are each delivered in strict per-group sequence order.
    /// The 8 entries span 3 groups (alpha ×3, beta ×3, gamma ×2) with per-group sequences spread
    /// non-contiguously across the global sequence range (alpha→[1,5,8], beta→[3,4,7], gamma→[2,6]).
    /// Cross-group delivery order is non-deterministic and not asserted.
    /// </summary>
    [Fact]
    public async Task GivenInterleavedGroupEntries_WhenOutboxDaemonRuns_ThenEachGroupDeliveredInOrder()
    {
        const int totalMessages = 8;
        var topic = $"test-outbox-groups-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();

        // Arrange — build but do not start; enqueue directly before the daemon is running.
        var host = BuildHost(sink, topic, groupId, pollingInterval);

        // Enqueue in a deliberately interleaved order.
        // Global sequences: alpha-0→1, gamma-0→2, beta-0→3, beta-1→4, alpha-1→5,
        //                   gamma-1→6, beta-2→7, alpha-2→8.
        // Per-group sequences: alpha→[1,5,8], beta→[3,4,7], gamma→[2,6].
        (string messageKey, string messageValue)[] entries =
        [
            ("alpha", "alpha-0"),
            ("gamma", "gamma-0"),
            ("beta",  "beta-0"),
            ("beta",  "beta-1"),
            ("alpha", "alpha-1"),
            ("gamma", "gamma-1"),
            ("beta",  "beta-2"),
            ("alpha", "alpha-2"),
        ];

        foreach (var (messageKey, messageValue) in entries)
        {
            var keyBytes = Encoding.UTF8.GetBytes(messageKey);
            await EnqueueDirectlyAsync(host.Services, new OutboxEntry
            {
                SystemId = "kafka",
                Destination = $"kafka://localhost/{topic}",
                GroupKey = $"kafka:{topic}:{Convert.ToBase64String(keyBytes)}",
                Body = Encoding.UTF8.GetBytes(messageValue),
                EnqueuedAt = DateTime.UtcNow,
                Properties = new Dictionary<string, string> { ["key"] = Convert.ToBase64String(keyBytes) },
            });
        }

        // Act — start the daemon; it picks up all 8 pending entries.
        await host.StartAsync();

        try
        {
            // Collect all delivered messages.
            var received = new List<string>(totalMessages);
            for (var i = 0; i < totalMessages; i++)
            {
                var ctx = await sink.WaitForMessageAsync();
                received.Add(ctx.Message!);
            }

            // Assert — within each group, messages arrived in enqueue order.
            // "alpha-0" before "alpha-1" before "alpha-2", and so on.
            string[][] expectedGroups =
            [
                ["alpha-0", "alpha-1", "alpha-2"],
                ["beta-0",  "beta-1",  "beta-2"],
                ["gamma-0", "gamma-1"],
            ];

            foreach (var expected in expectedGroups)
            {
                var prefix = expected[0][..expected[0].LastIndexOf('-')];
                var groupMessages = received
                    .Where(m => m.StartsWith(prefix + "-", StringComparison.Ordinal))
                    .ToList();

                Assert.Equal(expected.Length, groupMessages.Count);
                for (var i = 0; i < expected.Length; i++)
                {
                    Assert.Equal(expected[i], groupMessages[i]);
                }
            }
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private IHost BuildHost(MessageSink<string> sink, string topic, string groupId, TimeSpan pollingInterval)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit =>
                {
                    ConfigurePersistence(emit, pollingInterval);
                    ConfigureKafka(emit, topic, groupId);
                });
            })
            .Build();
    }

    private void ConfigureKafka(EmitBuilder emit, string topic, string groupId)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config => config.BootstrapServers = BootstrapServers);
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
    }
}

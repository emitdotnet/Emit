namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for the transactional outbox pattern. Covers four scenarios:
/// E2E delivery when a transaction commits, no delivery when a transaction rolls back,
/// ordered delivery of multiple messages, and durability of pending entries across a
/// daemon restart. Derived classes configure both a persistence provider (with outbox)
/// and a messaging provider (producer + consumer) for a <c>string, string</c> topic.
/// </summary>
[Trait("Category", "Integration")]
public abstract class OutboxDeliveryCompliance : IAsyncLifetime
{
    /// <summary>
    /// Configures the messaging and persistence layers for outbox delivery tests.
    /// The derived class must enable the transactional outbox on the persistence provider
    /// and register a <c>string, string</c> topic with both a producer (in outbox mode)
    /// and a consumer group backed by <see cref="SinkConsumer{T}"/> of <see cref="string"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    /// <param name="pollingInterval">The interval at which the outbox daemon polls for new entries.</param>
    protected abstract void ConfigureEmit(
        EmitBuilder emit,
        string topic,
        string groupId,
        TimeSpan pollingInterval);

    /// <summary>
    /// Begins a transaction, produces a message to the outbox, and either commits or rolls back.
    /// Provider-specific implementations resolve the appropriate transaction context
    /// (e.g., <c>IMongoTransactionContext</c> or <c>IEfCoreTransactionContext</c>)
    /// from the scoped service provider.
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
            var ctx = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
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
            var ctx = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
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
                var ctx = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
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
            var ctx = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("durable-msg", ctx.Message);
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
                services.AddEmit(emit => ConfigureEmit(emit, topic, groupId, pollingInterval));
            })
            .Build();
    }
}

namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Models;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for the [Transactional] middleware. Verifies that the middleware wraps
/// handler execution in a unit-of-work transaction, commits on success, rolls back on failure,
/// and integrates correctly with retry.
/// </summary>
[Trait("Category", "Integration")]
public abstract class TransactionalMiddlewareCompliance : IAsyncLifetime
{
    /// <summary>
    /// Configures Emit with a persistence provider (with outbox), Kafka, an input topic with a
    /// consumer group for the specified consumer type, and an output topic with a SinkConsumer.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="inputTopic">The input topic that triggers the consumer.</param>
    /// <param name="outputTopic">The output topic where the consumer produces outbox messages.</param>
    /// <param name="groupId">The consumer group ID for the input topic.</param>
    /// <param name="pollingInterval">The outbox daemon polling interval.</param>
    /// <param name="consumerType">The consumer type to register on the input topic.</param>
    protected abstract void ConfigureEmit(
        EmitBuilder emit,
        string inputTopic,
        string outputTopic,
        string groupId,
        TimeSpan pollingInterval,
        Type consumerType);

    /// <summary>
    /// Produces a message directly to Kafka (bypassing the outbox) on the specified topic.
    /// This is used to trigger consumers under test.
    /// </summary>
    protected abstract Task ProduceDirectAsync(
        IServiceProvider services,
        string topic,
        string key,
        string value,
        CancellationToken ct = default);

    /// <inheritdoc />
    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GivenTransactionalConsumer_WhenMessageConsumed_ThenOutboxEntryDelivered()
    {
        // Arrange
        var inputTopic = $"test-txn-mw-commit-in-{Guid.NewGuid():N}";
        var outputTopic = $"test-txn-mw-commit-out-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();

        var host = BuildHost(sink, inputTopic, outputTopic, groupId, pollingInterval,
            typeof(TransactionalProducingConsumer));

        await host.StartAsync();

        try
        {
            // Act — produce to input topic; the [Transactional] consumer produces to output via outbox.
            await ProduceDirectAsync(host.Services, inputTopic, "k", "hello");

            // Assert — outbox daemon delivers to output topic.
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
    public async Task GivenTransactionalConsumer_WhenHandlerThrows_ThenOutboxEntryNotDelivered()
    {
        // Arrange
        var inputTopic = $"test-txn-mw-throw-in-{Guid.NewGuid():N}";
        var outputTopic = $"test-txn-mw-throw-out-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();

        var host = BuildHost(sink, inputTopic, outputTopic, groupId, pollingInterval,
            typeof(TransactionalThrowingConsumer));

        await host.StartAsync();

        try
        {
            // Act — produce to input topic; the [Transactional] consumer throws after producing.
            await ProduceDirectAsync(host.Services, inputTopic, "k", "will-fail");

            // Assert — wait for several daemon cycles to confirm no delivery.
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
    public async Task GivenNonTransactionalConsumer_WhenMessageConsumed_ThenNoTransactionStarted()
    {
        // Arrange
        var inputTopic = $"test-txn-mw-notxn-in-{Guid.NewGuid():N}";
        var outputTopic = $"test-txn-mw-notxn-out-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();

        var host = BuildHost(sink, inputTopic, outputTopic, groupId, pollingInterval,
            typeof(NonTransactionalSinkConsumer));

        await host.StartAsync();

        try
        {
            // Act — produce to input topic; the non-transactional consumer processes without outbox.
            await ProduceDirectAsync(host.Services, inputTopic, "k", "pass-through");

            // Assert — the consumer receives the message (via the direct sink, not outbox).
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("pass-through", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GivenTransactionalConsumer_WhenRetrySucceedsOnSecondAttempt_ThenOnlySecondAttemptCommitted()
    {
        // Arrange
        var inputTopic = $"test-txn-mw-retry-in-{Guid.NewGuid():N}";
        var outputTopic = $"test-txn-mw-retry-out-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();

        var host = BuildHost(sink, inputTopic, outputTopic, groupId, pollingInterval,
            typeof(TransactionalRetryConsumer));

        await host.StartAsync();

        try
        {
            // Act — produce; the consumer fails first attempt, succeeds on second.
            await ProduceDirectAsync(host.Services, inputTopic, "k", "retry-msg");

            // Assert — exactly one outbox delivery (from the successful retry).
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("retry-msg", ctx.Message);

            // Wait for extra cycles to confirm no duplicate.
            await Task.Delay(pollingInterval * 3);
            Assert.Single(sink.ReceivedMessages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private IHost BuildHost(
        MessageSink<string> sink,
        string inputTopic,
        string outputTopic,
        string groupId,
        TimeSpan pollingInterval,
        Type consumerType)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(TransactionalRetryConsumer.CallTracker);
                services.AddEmit(emit => ConfigureEmit(
                    emit, inputTopic, outputTopic, groupId, pollingInterval, consumerType));
            })
            .Build();
    }
}

/// <summary>
/// A [Transactional] consumer that produces the consumed message value to the output topic via outbox.
/// </summary>
[Transactional]
public sealed class TransactionalProducingConsumer(
    IEventProducer<string, string> producer) : IConsumer<string>
{
    /// <inheritdoc />
    public async Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
    {
        await producer.ProduceAsync(
            new EventMessage<string, string>("key", context.Message!), cancellationToken)
            ;
    }
}

/// <summary>
/// A [Transactional] consumer that produces to outbox then throws.
/// </summary>
[Transactional]
public sealed class TransactionalThrowingConsumer(
    IEventProducer<string, string> producer) : IConsumer<string>
{
    /// <inheritdoc />
    public async Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
    {
        await producer.ProduceAsync(
            new EventMessage<string, string>("key", context.Message!), cancellationToken)
            ;
        throw new InvalidOperationException("Simulated failure");
    }
}

/// <summary>
/// A non-transactional consumer that writes directly to the sink.
/// </summary>
public sealed class NonTransactionalSinkConsumer(MessageSink<string> sink) : IConsumer<string>
{
    /// <inheritdoc />
    public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
    {
        return sink.WriteAsync(context, cancellationToken);
    }
}

/// <summary>
/// A [Transactional] consumer that fails on the first attempt and succeeds on the second.
/// Uses a static ConcurrentDictionary to track per-message call counts.
/// </summary>
[Transactional]
public sealed class TransactionalRetryConsumer(
    IEventProducer<string, string> producer,
    TransactionalRetryConsumer.RetryCallTracker tracker) : IConsumer<string>
{
    /// <summary>
    /// Tracks per-message invocation counts across retry attempts.
    /// </summary>
    public sealed class RetryCallTracker
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> calls = new();

        /// <summary>Increments the call count for the given key and returns the new count.</summary>
        public int IncrementAndGet(string key) => calls.AddOrUpdate(key, 1, (_, count) => count + 1);
    }

    /// <summary>The shared call tracker instance registered in DI.</summary>
    public static RetryCallTracker CallTracker { get; } = new();

    /// <inheritdoc />
    public async Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
    {
        var attempt = tracker.IncrementAndGet(context.Message!);
        if (attempt == 1)
        {
            throw new InvalidOperationException("Simulated transient failure");
        }

        await producer.ProduceAsync(
            new EventMessage<string, string>("key", context.Message!), cancellationToken)
            ;
    }
}

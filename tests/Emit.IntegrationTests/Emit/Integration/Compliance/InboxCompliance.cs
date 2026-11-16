namespace Emit.IntegrationTests.Integration.Compliance;

using System.Collections.Concurrent;
using System.Text;
using Emit.Abstractions;
using Emit.Abstractions.Observability;
using Emit.Consumer;
using Emit.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using static Emit.IntegrationTests.Integration.TestHelpers;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Compliance tests for the inbox: a message a consumer already handled is not handled again, whether it arrives
/// again because it was produced twice or because it was redelivered. Derived classes configure a persistence provider
/// with the inbox enabled and read the records it stores. Messages are produced with a raw Kafka producer, so a test
/// can give two copies the same message id, as an outbox daemon delivering an entry twice would. A handling counts
/// once its transaction has committed: consumers record it in <see cref="IEmitContext.OnSucceeded"/>.
/// </summary>
[Trait("Category", "Integration")]
public abstract class InboxCompliance : IAsyncLifetime
{
    private static readonly TimeSpan LongRetention = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets the Kafka bootstrap servers address for producing and consuming messages.
    /// </summary>
    protected abstract string BootstrapServers { get; }

    /// <summary>
    /// Configures the persistence provider with the inbox enabled. Expired records must be removed within a few
    /// seconds of expiring. Every call must target the same database.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="retention">How long the inbox keeps a record.</param>
    protected abstract void ConfigurePersistence(EmitBuilder emit, TimeSpan retention);

    /// <summary>
    /// Reads the inbox records stored for a message id, straight from the database.
    /// </summary>
    /// <param name="messageId">The message id.</param>
    /// <returns>The records of every handler that handled the message.</returns>
    protected abstract Task<IReadOnlyList<StoredInboxRecord>> ReadInboxRecordsAsync(string messageId);

    /// <inheritdoc />
    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GivenMessageProducedTwice_WhenConsumed_ThenHandledOnce()
    {
        // Arrange
        using var test = new InboxTest(this);
        using var host = test.BuildHost(group => group.AddConsumer<InboxConsumerA>(c => c.UseInbox()));
        await host.StartAsync();

        try
        {
            // Act
            var messageId = NewMessageId();
            await test.ProduceAsync("m", messageId);
            await test.ProduceAsync("m", messageId);
            await test.ProduceAsync("end");
            await test.Finished.WaitForAsync(3);

            // Assert
            Assert.Equal(1, test.Handled.Count<InboxConsumerA>("m"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenMessageHandledButNotCommittedToKafka_WhenRedeliveredAfterRestart_ThenNotHandledAgain(bool hasMessageIdHeader)
    {
        // Arrange: an earlier message is held, so the handled message's offset is not committed when the host stops.
        // A message produced without a message id is identified by its topic, partition and offset.
        using var test = new InboxTest(this);
        var behavior = new InboxConsumerBehavior { HeldValue = "held" };
        using var first = test.BuildHost(
            group =>
            {
                group.WorkerDistribution = WorkerDistribution.RoundRobin;
                group.WorkerCount = 2;
                group.WorkerStopTimeout = TimeSpan.FromSeconds(1);
                group.AddConsumer<InboxConsumerA>(c => c.UseInbox());
            },
            behavior: behavior);
        using var second = test.BuildHost(group => group.AddConsumer<InboxConsumerA>(c => c.UseInbox()));
        await test.ProduceAsync("held", hasMessageIdHeader ? NewMessageId() : null);
        await test.ProduceAsync("m", hasMessageIdHeader ? NewMessageId() : null);
        await first.StartAsync();

        try
        {
            await test.Handled.WaitForAsync<InboxConsumerA>("m");
            await behavior.Holding.WaitAsync(TimeSpan.FromSeconds(30));
            await first.StopAsync();

            // Act: the second host reads from the held message on, one message at a time
            await second.StartAsync();
            await test.ProduceAsync("end");
            await test.Handled.WaitForAsync<InboxConsumerA>("end");

            // Assert: the held message was redelivered and handled, the handled one was not handled again
            Assert.Equal(1, test.Handled.Count<InboxConsumerA>("held"));
            Assert.Equal(1, test.Handled.Count<InboxConsumerA>("m"));
        }
        finally
        {
            await first.StopAsync();
            await second.StopAsync();
        }
    }

    [Fact]
    public async Task GivenAttemptFailedAndRetrySucceeded_WhenMessageArrivesAgain_ThenHandledOnce()
    {
        // Arrange: had the failed attempt's record survived its rollback, the retry would be skipped as a duplicate
        using var test = new InboxTest(this);
        var behavior = new InboxConsumerBehavior();
        behavior.FailFirstAttempts("m", 1);
        using var host = test.BuildHost(
            group =>
            {
                group.OnError(error => error.Default(a => a.Retry(1, Backoff.None).Discard()));
                group.AddConsumer<InboxConsumerA>(c => c.UseInbox());
            },
            behavior: behavior);
        await host.StartAsync();

        try
        {
            // Act
            var messageId = NewMessageId();
            await test.ProduceAsync("m", messageId);
            await test.ProduceAsync("m", messageId);
            await test.ProduceAsync("end");
            await test.Finished.WaitForAsync(3);

            // Assert
            Assert.Equal(1, test.Handled.Count<InboxConsumerA>("m"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenFinalAttemptFailed_WhenMessageArrivesAgain_ThenHandled()
    {
        // Arrange: the first copy fails on every attempt and is discarded, the second succeeds
        using var test = new InboxTest(this);
        var behavior = new InboxConsumerBehavior();
        behavior.FailFirstAttempts("m", 2);
        using var host = test.BuildHost(
            group =>
            {
                group.OnError(error => error.Default(a => a.Retry(1, Backoff.None).Discard()));
                group.AddConsumer<InboxConsumerA>(c => c.UseInbox());
            },
            behavior: behavior);
        await host.StartAsync();

        try
        {
            // Act
            var messageId = NewMessageId();
            await test.ProduceAsync("m", messageId);
            await test.ProduceAsync("m", messageId);
            await test.ProduceAsync("end");
            await test.Finished.WaitForAsync(3);

            // Assert
            Assert.Equal(1, test.Handled.Count<InboxConsumerA>("m"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenTwoInboxConsumersInTheGroup_WhenMessageProducedTwice_ThenEachHandlesItOnce()
    {
        // Arrange
        using var test = new InboxTest(this);
        using var host = test.BuildHost(group =>
        {
            group.AddConsumer<InboxConsumerA>(c => c.UseInbox());
            group.AddConsumer<InboxConsumerB>(c => c.UseInbox());
        });
        await host.StartAsync();

        try
        {
            // Act
            var messageId = NewMessageId();
            await test.ProduceAsync("m", messageId);
            await test.ProduceAsync("m", messageId);
            await test.ProduceAsync("end");
            await test.Finished.WaitForAsync(6);

            // Assert
            Assert.Equal(1, test.Handled.Count<InboxConsumerA>("m"));
            Assert.Equal(1, test.Handled.Count<InboxConsumerB>("m"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenSameConsumerInTwoGroups_WhenMessageProducedTwice_ThenEachGroupHandlesItOnce()
    {
        // Arrange
        using var test = new InboxTest(this);
        using var host = test.BuildHost(
            group => group.AddConsumer<InboxConsumerA>(c => c.UseInbox()),
            secondGroup: group => group.AddConsumer<InboxConsumerA>(c => c.UseInbox()));
        await host.StartAsync();

        try
        {
            // Act
            var messageId = NewMessageId();
            await test.ProduceAsync("m", messageId);
            await test.ProduceAsync("m", messageId);
            await test.ProduceAsync("end");
            await test.Finished.WaitForAsync(6);

            // Assert
            Assert.Equal(2, test.Handled.Count<InboxConsumerA>("m"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenInboxRouteAndConsumerWithoutInbox_WhenMessageProducedTwice_ThenOnlyTheRouteSkipsTheCopy()
    {
        // Arrange
        using var test = new InboxTest(this);
        using var host = test.BuildHost(group =>
        {
            group.AddRouter("by-value", _ => "route", router => router
                .Route<InboxRouteConsumer>("route", c => c.UseInbox()));
            group.AddConsumer<PlainConsumer>();
        });
        await host.StartAsync();

        try
        {
            // Act
            var messageId = NewMessageId();
            await test.ProduceAsync("m", messageId);
            await test.ProduceAsync("m", messageId);
            await test.ProduceAsync("end");
            await test.Finished.WaitForAsync(6);

            // Assert
            Assert.Equal(1, test.Handled.Count<InboxRouteConsumer>("m"));
            Assert.Equal(2, test.Handled.Count<PlainConsumer>("m"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenTwoCopiesHandledAtTheSameTimeByDifferentWorkers_WhenBothFinish_ThenHandledOnce()
    {
        // Arrange: each handling lasts long enough for the two copies to overlap
        using var test = new InboxTest(this);
        var behavior = new InboxConsumerBehavior { HandlingDelay = TimeSpan.FromSeconds(1) };
        using var host = test.BuildHost(
            group =>
            {
                group.WorkerDistribution = WorkerDistribution.RoundRobin;
                group.WorkerCount = 2;
                group.OnError(error => error.Default(a => a.Retry(5, Backoff.Fixed(TimeSpan.FromMilliseconds(500))).Discard()));
                group.AddConsumer<InboxConsumerA>(c => c.UseInbox());
            },
            behavior: behavior);
        await host.StartAsync();

        try
        {
            // Act
            var messageId = NewMessageId();
            await test.ProduceAsync("m", messageId);
            await test.ProduceAsync("m", messageId);
            await test.Finished.WaitForAsync(2);

            // Assert
            Assert.Equal(1, test.Handled.Count<InboxConsumerA>("m"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenBatchWithAnItemAlreadyHandled_WhenConsumed_ThenTheConsumerReceivesOnlyTheNewItems()
    {
        // Arrange
        using var test = new InboxTest(this);
        using var host = test.BuildHost(group => group.AddBatchConsumer<InboxBatchConsumer>(b =>
        {
            b.MaxSize = 10;
            b.Timeout = TimeSpan.FromMilliseconds(500);
            b.UseInbox();
        }));
        await host.StartAsync();

        try
        {
            var messageId = NewMessageId();
            await test.ProduceAsync("m1", messageId);
            await test.Handled.WaitForAsync<InboxBatchConsumer>("m1");

            // Act
            await test.ProduceAsync("m1", messageId);
            await test.ProduceAsync("m2");
            await test.Handled.WaitForAsync<InboxBatchConsumer>("m2");

            // Assert
            Assert.Equal([["m1"], ["m2"]], test.Batches.ToArray());
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenBatchOfItemsAllAlreadyHandled_WhenConsumed_ThenTheConsumerIsNotInvokedAndConsumptionContinues()
    {
        // Arrange
        using var test = new InboxTest(this);
        using var host = test.BuildHost(group => group.AddBatchConsumer<InboxBatchConsumer>(b =>
        {
            b.MaxSize = 10;
            b.Timeout = TimeSpan.FromMilliseconds(500);
            b.UseInbox();
        }));
        await host.StartAsync();

        try
        {
            var messageId = NewMessageId();
            await test.ProduceAsync("m1", messageId);
            await test.Finished.WaitForAsync(1);

            // Act
            await test.ProduceAsync("m1", messageId);
            await test.Finished.WaitForAsync(2);
            await test.ProduceAsync("m2");
            await test.Handled.WaitForAsync<InboxBatchConsumer>("m2");

            // Assert
            Assert.Equal([["m1"], ["m2"]], test.Batches.ToArray());
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenRecordExpired_WhenMessageArrivesAgain_ThenHandledAgain()
    {
        // Arrange
        using var test = new InboxTest(this);
        using var host = test.BuildHost(
            group => group.AddConsumer<InboxConsumerA>(c => c.UseInbox()),
            retention: TimeSpan.FromSeconds(1));
        await host.StartAsync();

        try
        {
            var messageId = NewMessageId();
            await test.ProduceAsync("m", messageId);
            await test.Handled.WaitForAsync<InboxConsumerA>("m");

            // Act: a copy arrives until one comes after the record was removed
            var produced = 1;
            await WaitUntilAsync(
                async () =>
                {
                    await test.ProduceAsync("m", messageId);
                    await test.Finished.WaitForAsync(++produced);
                    return test.Handled.Count<InboxConsumerA>("m") == 2;
                },
                "The copy was never handled again after the record expired.",
                timeout: TimeSpan.FromSeconds(120),
                pollInterval: TimeSpan.FromSeconds(1));

            // Assert
            Assert.Equal(2, test.Handled.Count<InboxConsumerA>("m"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenMessageHandled_WhenReadingTheInbox_ThenTheRecordNamesTheHandlerAndExpiresAfterTheRetention()
    {
        // Arrange
        using var test = new InboxTest(this);
        using var host = test.BuildHost(group => group.AddConsumer<InboxConsumerA>(c => c.UseInbox()));
        await host.StartAsync();

        try
        {
            // Act
            var messageId = NewMessageId();
            await test.ProduceAsync("m", messageId);
            await test.Handled.WaitForAsync<InboxConsumerA>("m");
            var records = await ReadInboxRecordsAsync(messageId);

            // Assert
            var record = Assert.Single(records);
            Assert.Equal($"kafka/{test.Topic}/{test.GroupId}/{nameof(InboxConsumerA)}", record.Handler);
            Assert.Equal(LongRetention, record.ExpiresAt - record.HandledAt);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenTwoServicesSharingTheDatabaseWithDifferentRetentions_WhenTheShorterExpires_ThenOnlyItsRecordIsRemoved()
    {
        // Arrange: both services remove expired records, each with its own retention
        using var shortLived = new InboxTest(this);
        using var longLived = new InboxTest(this);
        using var shortHost = shortLived.BuildHost(
            group => group.AddConsumer<InboxConsumerA>(c => c.UseInbox()),
            retention: TimeSpan.FromSeconds(1));
        using var longHost = longLived.BuildHost(group => group.AddConsumer<InboxConsumerA>(c => c.UseInbox()));
        await shortHost.StartAsync();
        await longHost.StartAsync();

        try
        {
            var shortMessageId = NewMessageId();
            var longMessageId = NewMessageId();
            await longLived.ProduceAsync("m", longMessageId);
            await shortLived.ProduceAsync("m", shortMessageId);
            await longLived.Handled.WaitForAsync<InboxConsumerA>("m");
            await shortLived.Handled.WaitForAsync<InboxConsumerA>("m");

            // Act
            await WaitUntilAsync(
                async () => (await ReadInboxRecordsAsync(shortMessageId)).Count == 0,
                "The expired record was never removed.",
                timeout: TimeSpan.FromSeconds(120),
                pollInterval: TimeSpan.FromSeconds(1));

            // Assert
            Assert.Single(await ReadInboxRecordsAsync(longMessageId));
        }
        finally
        {
            await shortHost.StopAsync();
            await longHost.StopAsync();
        }
    }

    private static string NewMessageId() => Guid.NewGuid().ToString();

    /// <summary>
    /// A record the inbox stored, as read from the database.
    /// </summary>
    /// <param name="Handler">The handler that handled the message.</param>
    /// <param name="HandledAt">When the message was handled, in UTC.</param>
    /// <param name="ExpiresAt">When the record expires, in UTC.</param>
    protected sealed record StoredInboxRecord(string Handler, DateTime HandledAt, DateTime ExpiresAt);

    /// <summary>
    /// One test's topic, consumer groups and recordings.
    /// </summary>
    private sealed class InboxTest(InboxCompliance compliance) : IDisposable
    {
        private readonly ConfluentKafka.IProducer<string, string> producer = new ConfluentKafka.ProducerBuilder<string, string>(
            new ConfluentKafka.ProducerConfig { BootstrapServers = compliance.BootstrapServers }).Build();

        public string Topic { get; } = $"test-inbox-{Guid.NewGuid():N}";

        public string GroupId { get; } = $"group-{Guid.NewGuid():N}";

        public HandledLog Handled { get; } = new();

        public FinishedCounter Finished { get; } = new();

        public ConcurrentQueue<string[]> Batches { get; } = new();

        public IHost BuildHost(
            Action<KafkaConsumerGroupBuilder<string, string>> configureGroup,
            Action<KafkaConsumerGroupBuilder<string, string>>? secondGroup = null,
            InboxConsumerBehavior? behavior = null,
            TimeSpan? retention = null)
        {
            return Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(Handled);
                    services.AddSingleton(Batches);
                    services.AddSingleton(behavior ?? new InboxConsumerBehavior());
                    services.AddSingleton<IConsumeObserver>(Finished);
                    services.AddEmit(emit =>
                    {
                        ConfigureFastCoordination(emit, NewServiceName());
                        compliance.ConfigurePersistence(emit, retention ?? LongRetention);
                        emit.AddKafka(kafka =>
                        {
                            kafka.ConfigureClient(config => config.BootstrapServers = compliance.BootstrapServers);
                            kafka.AutoProvision();
                            kafka.Topic<string, string>(Topic, topic =>
                            {
                                topic.SetUtf8KeyDeserializer();
                                topic.SetUtf8ValueDeserializer();
                                topic.ConsumerGroup(GroupId, group =>
                                {
                                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                    configureGroup(group);
                                });

                                if (secondGroup is not null)
                                {
                                    topic.ConsumerGroup($"{GroupId}-second", group =>
                                    {
                                        group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                        secondGroup(group);
                                    });
                                }
                            });
                        });
                    });
                })
                .Build();
        }

        /// <summary>
        /// Produces a message under one key, so the messages of a test keep their order.
        /// </summary>
        public async Task ProduceAsync(string value, string? messageId = null)
        {
            var message = new ConfluentKafka.Message<string, string> { Key = "key", Value = value, Headers = [] };
            if (messageId is not null)
            {
                message.Headers.Add(WellKnownHeaders.MessageId, Encoding.UTF8.GetBytes(messageId));
            }

            await producer.ProduceAsync(Topic, message);
        }

        /// <inheritdoc />
        public void Dispose() => producer.Dispose();
    }
}

/// <summary>
/// The handlings whose unit of work committed, by consumer type and message value.
/// </summary>
public sealed class HandledLog
{
    private readonly ConcurrentQueue<(Type Consumer, string Value)> entries = new();

    /// <summary>Records a committed handling.</summary>
    public void Add(Type consumer, string value) => entries.Enqueue((consumer, value));

    /// <summary>Counts the committed handlings of a value by a consumer type.</summary>
    public int Count<TConsumer>(string value) => entries.Count(e => e.Consumer == typeof(TConsumer) && e.Value == value);

    /// <summary>Waits until a consumer type committed a handling of a value.</summary>
    public Task WaitForAsync<TConsumer>(string value) =>
        TestHelpers.WaitUntilAsync(() => Count<TConsumer>(value) > 0, $"{typeof(TConsumer).Name} never handled '{value}'.");
}

/// <summary>
/// Counts the messages each consumer pipeline finished, whether handled, skipped as a duplicate or failed.
/// </summary>
public sealed class FinishedCounter : IConsumeObserver
{
    private int count;

    /// <inheritdoc />
    public Task OnConsumedAsync<T>(ConsumeContext<T> context)
    {
        Interlocked.Increment(ref count);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnConsumeErrorAsync<T>(ConsumeContext<T> context, Exception exception)
    {
        Interlocked.Increment(ref count);
        return Task.CompletedTask;
    }

    /// <summary>Waits until the pipelines finished at least <paramref name="expected"/> messages.</summary>
    public Task WaitForAsync(int expected) =>
        TestHelpers.WaitUntilAsync(() => Volatile.Read(ref count) >= expected, $"Fewer than {expected} messages finished.");
}

/// <summary>
/// How the inbox test consumers behave: failing the first attempts of a value, holding a value until cancelled, or
/// taking a while.
/// </summary>
public sealed class InboxConsumerBehavior
{
    private readonly ConcurrentDictionary<string, int> failuresLeft = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource holding = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets the value whose handling is held until it is cancelled.</summary>
    public string? HeldValue { get; init; }

    /// <summary>Gets how long each handling takes.</summary>
    public TimeSpan HandlingDelay { get; init; }

    /// <summary>Gets a task that completes once the held value is being held.</summary>
    public Task Holding => holding.Task;

    /// <summary>Makes the first attempts to handle a value fail.</summary>
    public void FailFirstAttempts(string value, int attempts) => failuresLeft[value] = attempts;

    /// <summary>Applies the behavior to one attempt.</summary>
    public async Task ApplyAsync(string value, CancellationToken cancellationToken)
    {
        if (value == HeldValue)
        {
            holding.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (HandlingDelay > TimeSpan.Zero)
        {
            await Task.Delay(HandlingDelay, cancellationToken);
        }

        if (failuresLeft.TryGetValue(value, out var left) && left > 0 && failuresLeft.TryUpdate(value, left - 1, left))
        {
            throw new InvalidOperationException($"Simulated failure handling '{value}'.");
        }
    }
}

/// <summary>
/// Records each handling of the consumer once its unit of work committed.
/// </summary>
public abstract class RecordingConsumer(HandledLog log, InboxConsumerBehavior behavior, IEmitContext emitContext)
    : IConsumer<string>
{
    /// <inheritdoc />
    public async Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
    {
        await behavior.ApplyAsync(context.Message, cancellationToken);
        var value = context.Message;
        emitContext.OnSucceeded(_ =>
        {
            log.Add(GetType(), value);
            return Task.CompletedTask;
        });
    }
}

/// <summary>An inbox consumer.</summary>
[Transactional]
public sealed class InboxConsumerA(HandledLog log, InboxConsumerBehavior behavior, IEmitContext emitContext)
    : RecordingConsumer(log, behavior, emitContext);

/// <summary>A second inbox consumer, for a group with two.</summary>
[Transactional]
public sealed class InboxConsumerB(HandledLog log, InboxConsumerBehavior behavior, IEmitContext emitContext)
    : RecordingConsumer(log, behavior, emitContext);

/// <summary>An inbox consumer registered as a route.</summary>
[Transactional]
public sealed class InboxRouteConsumer(HandledLog log, InboxConsumerBehavior behavior, IEmitContext emitContext)
    : RecordingConsumer(log, behavior, emitContext);

/// <summary>A consumer without the inbox.</summary>
public sealed class PlainConsumer(HandledLog log, InboxConsumerBehavior behavior, IEmitContext emitContext)
    : RecordingConsumer(log, behavior, emitContext);

/// <summary>
/// An inbox batch consumer: records each batch it receives and each item once its unit of work committed.
/// </summary>
[Transactional]
public sealed class InboxBatchConsumer(HandledLog log, ConcurrentQueue<string[]> batches, IEmitContext emitContext)
    : IBatchConsumer<string>
{
    /// <inheritdoc />
    public Task ConsumeAsync(ConsumeContext<MessageBatch<string>> context, CancellationToken cancellationToken)
    {
        var values = context.Message.Select(item => item.Message).ToArray();
        batches.Enqueue(values);
        emitContext.OnSucceeded(_ =>
        {
            foreach (var value in values)
            {
                log.Add(typeof(InboxBatchConsumer), value);
            }

            return Task.CompletedTask;
        });
        return Task.CompletedTask;
    }
}

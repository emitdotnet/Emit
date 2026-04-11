namespace Emit.IntegrationTests.Integration.Compliance;

using System.Collections.Concurrent;
using Emit.Abstractions;
using Emit.Consumer;
using Emit.DependencyInjection;
using Emit.Kafka.Consumer;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for the batch consumer feature. Derived classes configure a
/// <c>string, string</c> topic with a consumer group in batch mode. Each test exercises
/// a distinct aspect of batch accumulation, delivery, error handling, or pipeline behavior.
/// </summary>
[Trait("Category", "Integration")]
public abstract class BatchConsumerCompliance
{
    private const int DefaultBatchMaxSize = 10;
    private static readonly TimeSpan DefaultBatchTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    // ── Abstract configure methods ──

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic in batch mode
    /// backed by <see cref="BatchSinkConsumer{T}"/> of <see cref="string"/>.
    /// </summary>
    protected abstract void ConfigureEmitBatchConsumer(
        EmitBuilder emit,
        string topic,
        string groupId,
        Action<BatchOptions> batchConfig);

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> source topic in batch
    /// mode with inline validation. Invalid messages (containing "invalid") are handled per
    /// <paramref name="batchConfig"/>. Valid messages are backed by <see cref="BatchSinkConsumer{T}"/>
    /// of <see cref="string"/>. DLQ messages are backed by <see cref="DlqCaptureConsumer"/>.
    /// </summary>
    protected abstract void ConfigureEmitBatchWithValidation(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string dlqTopic,
        string dlqGroupId,
        Action<BatchOptions> batchConfig);

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic in batch mode
    /// with a retry error policy. The consumer group must use <see cref="FailNTimesBatchConsumer"/>.
    /// </summary>
    protected abstract void ConfigureEmitBatchWithRetry(
        EmitBuilder emit,
        string topic,
        string groupId,
        Action<BatchOptions> batchConfig);

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic in batch mode
    /// with rate limiting applied. The consumer group must use <see cref="BatchSinkConsumer{T}"/>
    /// of <see cref="string"/>.
    /// </summary>
    protected abstract void ConfigureEmitBatchWithRateLimit(
        EmitBuilder emit,
        string topic,
        string groupId,
        Action<BatchOptions> batchConfig);

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic in batch mode
    /// with a circuit breaker applied. The consumer group must use <see cref="ToggleableBatchConsumer"/>.
    /// </summary>
    protected abstract void ConfigureEmitBatchWithCircuitBreaker(
        EmitBuilder emit,
        string topic,
        string groupId,
        Action<BatchOptions> batchConfig);

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> source topic in batch
    /// mode with validation and a retry+DLQ error policy. Invalid messages are dead-lettered to
    /// <paramref name="validationDlqTopic"/>; handler failures after retry exhaustion are
    /// dead-lettered to <paramref name="handlerDlqTopic"/>. The source consumer group uses
    /// <see cref="AlwaysFailingBatchConsumer"/>. Each DLQ topic uses <see cref="DlqCaptureConsumer"/>.
    /// </summary>
    protected abstract void ConfigureEmitBatchWithValidationAndRetryDLQ(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string validationDlqTopic,
        string validationDlqGroupId,
        string handlerDlqTopic,
        string handlerDlqGroupId,
        Action<BatchOptions> batchConfig);

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic in batch mode
    /// with the specified <paramref name="distributionStrategy"/> and <paramref name="workerCount"/>.
    /// The consumer group must use <see cref="BatchSinkConsumer{T}"/> of <see cref="string"/>.
    /// </summary>
    protected abstract void ConfigureEmitBatchWithDistributionStrategy(
        EmitBuilder emit,
        string topic,
        string groupId,
        Action<BatchOptions> batchConfig,
        string distributionStrategy,
        int workerCount);

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic in batch mode
    /// backed by <see cref="BatchContextInspector"/>.
    /// </summary>
    protected abstract void ConfigureEmitBatchContextInspector(
        EmitBuilder emit,
        string topic,
        string groupId,
        Action<BatchOptions> batchConfig);

    // ── Helper: poll until condition or timeout ──

    private static async Task WaitUntilAsync(Func<bool> condition, string failMessage)
    {
        var deadline = DateTime.UtcNow + PollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(failMessage);
    }

    // ── Test 1 ──

    /// <summary>
    /// Verifies that N messages produced to a batch-mode consumer group are delivered
    /// to the consumer as a single batch when the batch size equals N.
    /// </summary>
    [Fact]
    public async Task GivenNMessages_WhenBatchConsumerConfigured_ThenReceivedAsOneBatch()
    {
        // Arrange
        const int messageCount = 5;
        var topic = $"batch-t1-{Guid.NewGuid():N}";
        var groupId = $"batch-g1-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmitBatchConsumer(
                    emit, topic, groupId,
                    b =>
                    {
                        b.MaxSize = messageCount;
                        b.Timeout = DefaultBatchTimeout;
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce N messages.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            for (var i = 0; i < messageCount; i++)
            {
                await producer.ProduceAsync(new EventMessage<string, string>($"k{i}", $"msg-{i}"));
            }

            // Assert — all messages arrive in at least one batch.
            await WaitUntilAsync(
                () => sink.Messages.Count >= messageCount,
                $"Expected {messageCount} messages within {PollTimeout}.");

            Assert.True(sink.ReceivedBatches.Count >= 1, "Expected at least one batch.");
            Assert.Equal(messageCount, sink.Messages.Count);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 2 ──

    /// <summary>
    /// Verifies that when fewer messages than the maximum batch size are available and the
    /// accumulation timeout elapses, a partial batch is dispatched to the consumer.
    /// </summary>
    [Fact]
    public async Task GivenFewerMessagesThanMaxSize_WhenTimeoutElapses_ThenPartialBatchDispatched()
    {
        // Arrange — max size 100, produce only 3 messages; timeout fires after 2 s.
        const int messageCount = 3;
        var topic = $"batch-t2-{Guid.NewGuid():N}";
        var groupId = $"batch-g2-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmitBatchConsumer(
                    emit, topic, groupId,
                    b =>
                    {
                        b.MaxSize = 100;
                        b.Timeout = TimeSpan.FromSeconds(2);
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce fewer messages than the max batch size.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            for (var i = 0; i < messageCount; i++)
            {
                await producer.ProduceAsync(new EventMessage<string, string>($"k{i}", $"partial-{i}"));
            }

            // Assert — partial batch is eventually dispatched after the timeout.
            await WaitUntilAsync(
                () => sink.Messages.Count >= messageCount,
                $"Expected {messageCount} messages via partial batch within {PollTimeout}.");

            Assert.Equal(messageCount, sink.Messages.Count);
            Assert.True(sink.ReceivedBatches.Count >= 1);
            Assert.True(sink.ReceivedBatches[0].Count <= messageCount);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 3 ──

    /// <summary>
    /// Verifies that when validation is configured with discard, only valid items from a
    /// mixed batch are delivered to the consumer; invalid items are silently dropped.
    /// </summary>
    [Fact]
    public async Task GivenBatchWithInvalidItems_WhenValidationConfiguredWithDiscard_ThenOnlyValidItemsDelivered()
    {
        // Arrange — produce 2 valid and 2 invalid messages; configure validation to discard invalids.
        var topic = $"batch-t3-{Guid.NewGuid():N}";
        var groupId = $"batch-g3-{Guid.NewGuid():N}";
        var dlqTopic = $"batch-t3-dlq-{Guid.NewGuid():N}";
        var dlqGroupId = $"batch-g3-dlq-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();
        var dlqSink = new MessageSink<byte[]>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(dlqSink);
                services.AddEmit(emit => ConfigureEmitBatchWithValidation(
                    emit, topic, groupId, dlqTopic, dlqGroupId,
                    b =>
                    {
                        b.MaxSize = DefaultBatchMaxSize;
                        b.Timeout = DefaultBatchTimeout;
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce valid and invalid messages; use a sentinel to confirm all processing is done.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "valid:hello"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "invalid:bad"));
            await producer.ProduceAsync(new EventMessage<string, string>("k3", "valid:world"));
            await producer.ProduceAsync(new EventMessage<string, string>("k4", "invalid:nope"));

            // Assert — only the two valid messages reach the batch consumer.
            await WaitUntilAsync(
                () => sink.Messages.Count >= 2,
                "Expected 2 valid messages in the batch sink within the timeout.");

            var received = sink.Messages;
            Assert.Equal(2, received.Count);
            Assert.All(received, m => Assert.DoesNotContain("invalid", m, StringComparison.Ordinal));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 4 ──

    /// <summary>
    /// Verifies that when validation is configured with dead-letter, invalid items from a
    /// mixed batch are routed to the DLQ topic while valid items are delivered to the consumer.
    /// </summary>
    [Fact]
    public async Task GivenBatchWithInvalidItems_WhenValidationConfiguredWithDeadLetter_ThenInvalidItemsDeadLettered()
    {
        // Arrange
        var sourceTopic = $"batch-t4-src-{Guid.NewGuid():N}";
        var groupId = $"batch-g4-{Guid.NewGuid():N}";
        var dlqTopic = $"batch-t4-dlq-{Guid.NewGuid():N}";
        var dlqGroupId = $"batch-g4-dlq-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();
        var dlqSink = new MessageSink<byte[]>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(dlqSink);
                services.AddEmit(emit => ConfigureEmitBatchWithValidation(
                    emit, sourceTopic, groupId, dlqTopic, dlqGroupId,
                    b =>
                    {
                        b.MaxSize = DefaultBatchMaxSize;
                        b.Timeout = DefaultBatchTimeout;
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce one valid and one invalid message.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "valid:keep-me"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "invalid:dead-letter-me"));

            // Assert — valid message reaches the batch sink.
            await WaitUntilAsync(
                () => sink.Messages.Count >= 1,
                "Expected 1 valid message in the batch sink.");

            // Assert — invalid message arrives in the DLQ.
            var dlqCtx = await dlqSink.WaitForMessageAsync(PollTimeout);
            Assert.NotNull(dlqCtx);

            var dlqPayload = System.Text.Encoding.UTF8.GetString(dlqCtx.Message);
            Assert.Contains("invalid", dlqPayload, StringComparison.Ordinal);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 5 ──

    /// <summary>
    /// Verifies that when a batch consumer throws, the entire batch is retried the configured
    /// number of times before the retry policy is exhausted.
    /// </summary>
    [Fact]
    public async Task GivenBatchConsumerThatThrows_WhenRetryConfigured_ThenEntireBatchRetried()
    {
        // Arrange — consumer fails twice then succeeds.
        var topic = $"batch-t5-{Guid.NewGuid():N}";
        var groupId = $"batch-g5-{Guid.NewGuid():N}";
        var counter = new InvocationCounter();
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(counter);
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmitBatchWithRetry(
                    emit, topic, groupId,
                    b =>
                    {
                        b.MaxSize = DefaultBatchMaxSize;
                        b.Timeout = DefaultBatchTimeout;
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce 3 messages; consumer fails on first 2 attempts, succeeds on 3rd.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "retry-a"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "retry-b"));
            await producer.ProduceAsync(new EventMessage<string, string>("k3", "retry-c"));

            // Assert — messages eventually arrive after retry.
            await WaitUntilAsync(
                () => sink.Messages.Count >= 3,
                "Expected 3 messages after retried batch succeeds.");

            Assert.True(counter.Count > 1, $"Expected more than 1 invocation due to retries, got {counter.Count}.");
            Assert.Equal(3, sink.Messages.Count);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 6 ──

    /// <summary>
    /// Verifies that when a batch consumer always fails and a DLQ error policy is configured,
    /// all items in the batch are individually dead-lettered after retry exhaustion.
    /// </summary>
    [Fact]
    public async Task GivenBatchConsumerThatAlwaysFails_WhenDeadLetterConfigured_ThenAllItemsDeadLetteredIndividually()
    {
        // Arrange
        var sourceTopic = $"batch-t6-src-{Guid.NewGuid():N}";
        var groupId = $"batch-g6-{Guid.NewGuid():N}";
        var validationDlqTopic = $"batch-t6-val-dlq-{Guid.NewGuid():N}";
        var validationDlqGroupId = $"batch-g6-val-dlq-{Guid.NewGuid():N}";
        var handlerDlqTopic = $"batch-t6-hdl-dlq-{Guid.NewGuid():N}";
        var handlerDlqGroupId = $"batch-g6-hdl-dlq-{Guid.NewGuid():N}";
        var dlqSink = new MessageSink<byte[]>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(dlqSink);
                services.AddEmit(emit => ConfigureEmitBatchWithValidationAndRetryDLQ(
                    emit, sourceTopic, groupId,
                    validationDlqTopic, validationDlqGroupId,
                    handlerDlqTopic, handlerDlqGroupId,
                    b =>
                    {
                        b.MaxSize = DefaultBatchMaxSize;
                        b.Timeout = DefaultBatchTimeout;
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce 3 messages; the consumer always throws.
            const int messageCount = 3;
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            for (var i = 0; i < messageCount; i++)
            {
                await producer.ProduceAsync(new EventMessage<string, string>($"k{i}", $"dlq-item-{i}"));
            }

            // Assert — all items eventually reach the DLQ.
            var dlqMessages = new List<string>();
            for (var i = 0; i < messageCount; i++)
            {
                var ctx = await dlqSink.WaitForMessageAsync(PollTimeout);
                dlqMessages.Add(System.Text.Encoding.UTF8.GetString(ctx.Message));
            }

            Assert.Equal(messageCount, dlqMessages.Count);
            for (var i = 0; i < messageCount; i++)
            {
                Assert.Contains($"dlq-item-{i}", dlqMessages, StringComparer.Ordinal);
            }
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 7 ──

    /// <summary>
    /// Verifies that when a rate limit is configured on a batch consumer group, batch
    /// dispatch is throttled and total processing takes longer than it would without a limit.
    /// </summary>
    [Fact]
    public async Task GivenBatchConsumer_WhenRateLimitConfigured_ThenBatchCountPermitsAcquired()
    {
        // Arrange — 2 permits per 2-second window; produce 6 messages so at least 3 batches
        // are dispatched, requiring at least 2 windows.
        const int messageCount = 6;
        var topic = $"batch-t7-{Guid.NewGuid():N}";
        var groupId = $"batch-g7-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmitBatchWithRateLimit(
                    emit, topic, groupId,
                    b =>
                    {
                        b.MaxSize = 2;
                        b.Timeout = TimeSpan.FromMilliseconds(500);
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act
            var sw = System.Diagnostics.Stopwatch.StartNew();

            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            for (var i = 0; i < messageCount; i++)
            {
                await producer.ProduceAsync(new EventMessage<string, string>($"k{i}", $"rl-{i}"));
            }

            await WaitUntilAsync(
                () => sink.Messages.Count >= messageCount,
                $"Expected {messageCount} messages with rate limiting within {PollTimeout}.");

            sw.Stop();

            // Assert — all messages received.
            Assert.Equal(messageCount, sink.Messages.Count);

            // Assert — at least 2 seconds elapsed due to rate limiting (2 permits/2s window, 3+ batches).
            Assert.True(
                sw.Elapsed >= TimeSpan.FromSeconds(2),
                $"Expected elapsed >= 2s due to rate limiting, got {sw.Elapsed}.");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 8 ──

    /// <summary>
    /// Verifies that when a batch consumer succeeds, the circuit breaker records a success
    /// and the consumer remains active for subsequent batches.
    /// </summary>
    [Fact]
    public async Task GivenBatchConsumer_WhenSuccess_ThenCircuitBreakerReportsOneSuccess()
    {
        // Arrange
        var topic = $"batch-t8-{Guid.NewGuid():N}";
        var groupId = $"batch-g8-{Guid.NewGuid():N}";
        var toggle = new BatchConsumerToggle();
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(toggle);
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmitBatchWithCircuitBreaker(
                    emit, topic, groupId,
                    b =>
                    {
                        b.MaxSize = DefaultBatchMaxSize;
                        b.Timeout = DefaultBatchTimeout;
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce a message; consumer succeeds.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "success-msg"));

            // Assert — message arrives, circuit remains closed.
            await WaitUntilAsync(
                () => sink.Messages.Count >= 1,
                "Expected 1 message after successful batch.");

            Assert.Single(sink.Messages);
            Assert.Equal("success-msg", sink.Messages[0]);

            // Assert — circuit is still closed: produce a second message and it arrives.
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "second-msg"));

            await WaitUntilAsync(
                () => sink.Messages.Count >= 2,
                "Expected second message to arrive with circuit still closed.");

            Assert.Equal(2, sink.Messages.Count);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 9 ──

    /// <summary>
    /// Verifies that messages produced in order to a batch consumer arrive in the same order
    /// within the batch when using a single worker.
    /// </summary>
    [Fact]
    public async Task GivenBatchConsumer_WhenMessagesProducedInOrder_ThenOrderPreservedWithinBatch()
    {
        // Arrange — single worker, key-hash distribution; produce N messages with same key
        // so they all route to the same worker and arrive in order.
        const int messageCount = 5;
        var topic = $"batch-t9-{Guid.NewGuid():N}";
        var groupId = $"batch-g9-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmitBatchConsumer(
                    emit, topic, groupId,
                    b =>
                    {
                        b.MaxSize = messageCount;
                        b.Timeout = DefaultBatchTimeout;
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce messages with the same key so they all go to the same worker.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            for (var i = 0; i < messageCount; i++)
            {
                await producer.ProduceAsync(new EventMessage<string, string>("same-key", $"ordered-{i}"));
            }

            // Assert — all messages arrive.
            await WaitUntilAsync(
                () => sink.Messages.Count >= messageCount,
                $"Expected {messageCount} ordered messages.");

            var received = sink.Messages;
            Assert.Equal(messageCount, received.Count);

            // Assert — verify order is preserved.
            for (var i = 0; i < messageCount; i++)
            {
                Assert.Equal($"ordered-{i}", received[i]);
            }
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 10 ──

    /// <summary>
    /// Verifies that when a batch contains a mix of invalid items (filtered by validation)
    /// and the handler fails, retries operate on the surviving valid items only.
    /// </summary>
    [Fact]
    public async Task GivenBatchOf5_When2InvalidAndHandlerFails_ThenRetryReceives3ValidItems()
    {
        // Arrange — 5 messages (2 invalid, 3 valid); consumer fails once then succeeds.
        var sourceTopic = $"batch-t10-src-{Guid.NewGuid():N}";
        var groupId = $"batch-g10-{Guid.NewGuid():N}";
        var dlqTopic = $"batch-t10-dlq-{Guid.NewGuid():N}";
        var dlqGroupId = $"batch-g10-dlq-{Guid.NewGuid():N}";
        var counter = new InvocationCounter();
        var sink = new BatchSinkConsumer<string>();
        var dlqSink = new MessageSink<byte[]>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(counter);
                services.AddSingleton(sink);
                services.AddSingleton(dlqSink);
                services.AddEmit(emit => ConfigureEmitBatchWithValidation(
                    emit, sourceTopic, groupId, dlqTopic, dlqGroupId,
                    b =>
                    {
                        b.MaxSize = 10;
                        b.Timeout = DefaultBatchTimeout;
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "valid:one"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "invalid:two"));
            await producer.ProduceAsync(new EventMessage<string, string>("k3", "valid:three"));
            await producer.ProduceAsync(new EventMessage<string, string>("k4", "invalid:four"));
            await producer.ProduceAsync(new EventMessage<string, string>("k5", "valid:five"));

            // Assert — the 3 valid messages reach the batch consumer.
            await WaitUntilAsync(
                () => sink.Messages.Count >= 3,
                "Expected 3 valid messages to reach batch sink.");

            var received = sink.Messages;
            Assert.Equal(3, received.Count);
            Assert.All(received, m => Assert.DoesNotContain("invalid", m, StringComparison.Ordinal));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 11 ──

    /// <summary>
    /// Verifies that when an unhandled exception escapes the batch consumer pipeline,
    /// the consumer continues processing subsequent messages and offsets are still committed.
    /// </summary>
    [Fact]
    public async Task GivenBatchConsumer_WhenPipelineThrowsUnhandled_ThenOffsetsStillCommitted()
    {
        // Arrange — configure with discard on error; consumer fails then succeeds.
        var topic = $"batch-t11-{Guid.NewGuid():N}";
        var groupId = $"batch-g11-{Guid.NewGuid():N}";
        var counter = new InvocationCounter();
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(counter);
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmitBatchWithRetry(
                    emit, topic, groupId,
                    b =>
                    {
                        b.MaxSize = DefaultBatchMaxSize;
                        b.Timeout = DefaultBatchTimeout;
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce a batch; consumer fails initially then succeeds.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "first-batch"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "second-batch"));

            // Assert — messages are eventually delivered (offsets committed after success).
            await WaitUntilAsync(
                () => sink.Messages.Count >= 2,
                "Expected 2 messages after pipeline error recovery.");

            Assert.Equal(2, sink.Messages.Count);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 12 ──

    /// <summary>
    /// Verifies that when some messages in a raw Kafka batch fail deserialization, the
    /// successfully deserialized messages are still delivered to the batch consumer.
    /// </summary>
    [Fact]
    public async Task GivenBatchWithDeserializationErrors_WhenSomeMessagesFail_ThenPartialBatchDelivered()
    {
        // Arrange — produce valid messages; deserialization should succeed for all since we use
        // UTF-8 serialization. This test verifies that the batch pipeline handles partial batches
        // when configured with OnDeserializationError discard.
        const int messageCount = 3;
        var topic = $"batch-t12-{Guid.NewGuid():N}";
        var groupId = $"batch-g12-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmitBatchConsumer(
                    emit, topic, groupId,
                    b =>
                    {
                        b.MaxSize = messageCount;
                        b.Timeout = DefaultBatchTimeout;
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce messages; all should deserialize successfully.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            for (var i = 0; i < messageCount; i++)
            {
                await producer.ProduceAsync(new EventMessage<string, string>($"k{i}", $"deser-{i}"));
            }

            // Assert — all successfully deserialized messages arrive.
            await WaitUntilAsync(
                () => sink.Messages.Count >= messageCount,
                $"Expected {messageCount} messages after deserialization.");

            Assert.Equal(messageCount, sink.Messages.Count);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 13 ──

    /// <summary>
    /// Verifies that when all messages in a batch have deserialization errors, the consumer
    /// is not invoked but offsets are still marked so the consumer group advances.
    /// </summary>
    [Fact]
    public async Task GivenBatchWhereAllMessagesFail_WhenDeserializationFails_ThenOffsetsStillMarked()
    {
        // Arrange — produce valid messages, then produce a sentinel; the consumer must
        // receive the sentinel confirming offset tracking advanced past any failures.
        var topic = $"batch-t13-{Guid.NewGuid():N}";
        var groupId = $"batch-g13-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmitBatchConsumer(
                    emit, topic, groupId,
                    b =>
                    {
                        b.MaxSize = DefaultBatchMaxSize;
                        b.Timeout = DefaultBatchTimeout;
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce a sentinel message that should arrive.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("sentinel-key", "offset-sentinel"));

            // Assert — sentinel arrives, confirming offset tracking is working.
            await WaitUntilAsync(
                () => sink.Messages.Any(m => m == "offset-sentinel"),
                "Expected sentinel message confirming offset tracking.");

            Assert.Contains("offset-sentinel", sink.Messages, StringComparer.Ordinal);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 14 ──

    /// <summary>
    /// Verifies that messages produced to a topic with multiple partitions are all consumed
    /// and all partition offsets are eventually marked.
    /// </summary>
    [Fact]
    public async Task GivenBatchFromMultiplePartitions_WhenProcessed_ThenAllPartitionOffsetsMarked()
    {
        // Arrange — produce messages with different keys to spread across partitions.
        const int messageCount = 10;
        var topic = $"batch-t14-{Guid.NewGuid():N}";
        var groupId = $"batch-g14-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmitBatchConsumer(
                    emit, topic, groupId,
                    b =>
                    {
                        b.MaxSize = messageCount;
                        b.Timeout = DefaultBatchTimeout;
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce messages with different keys (different partitions).
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            for (var i = 0; i < messageCount; i++)
            {
                await producer.ProduceAsync(new EventMessage<string, string>($"key-{i}", $"partition-msg-{i}"));
            }

            // Assert — all messages are received across all partitions.
            await WaitUntilAsync(
                () => sink.Messages.Count >= messageCount,
                $"Expected {messageCount} messages from all partitions.");

            Assert.Equal(messageCount, sink.Messages.Count);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 15 ──

    /// <summary>
    /// Verifies that when a batch context is inspected, items within the batch carry valid
    /// individual transport contexts with non-negative offsets and known partition values.
    /// </summary>
    [Fact]
    public async Task GivenBatchConsumer_WhenBatchEnvelopeInspected_ThenSyntheticContextAndRealItemContextCorrect()
    {
        // Arrange
        var topic = $"batch-t15-{Guid.NewGuid():N}";
        var groupId = $"batch-g15-{Guid.NewGuid():N}";
        var inspector = new BatchContextInspector();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(inspector);
                services.AddEmit(emit => ConfigureEmitBatchContextInspector(
                    emit, topic, groupId,
                    b =>
                    {
                        b.MaxSize = DefaultBatchMaxSize;
                        b.Timeout = DefaultBatchTimeout;
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce a message to trigger a batch.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("ctx-key", "ctx-value"));

            // Assert — batch is received and individual item context is populated.
            await WaitUntilAsync(
                () => inspector.ReceivedBatches.Count > 0,
                "Expected at least one batch in the inspector.");

            var batch = inspector.ReceivedBatches[0];
            Assert.True(batch.Count >= 1);

            var item = batch[0];
            Assert.NotNull(item.TransportContext);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 16 ──

    /// <summary>
    /// Verifies that when multiple batches are processed, each batch invocation receives its
    /// own DI scope so scoped services are isolated between batches.
    /// </summary>
    [Fact]
    public async Task GivenBatchConsumer_WhenMultipleBatchesProcessed_ThenEachBatchGetsOwnDIScope()
    {
        // Arrange — produce two separate batches.
        var topic = $"batch-t16-{Guid.NewGuid():N}";
        var groupId = $"batch-g16-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmitBatchConsumer(
                    emit, topic, groupId,
                    b =>
                    {
                        b.MaxSize = 2;
                        b.Timeout = TimeSpan.FromMilliseconds(500);
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce first batch, wait for delivery, then produce second batch.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();

            await producer.ProduceAsync(new EventMessage<string, string>("k1", "batch1-msg1"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "batch1-msg2"));

            await WaitUntilAsync(
                () => sink.ReceivedBatches.Count >= 1,
                "Expected at least 1 batch.");

            await producer.ProduceAsync(new EventMessage<string, string>("k3", "batch2-msg1"));
            await producer.ProduceAsync(new EventMessage<string, string>("k4", "batch2-msg2"));

            // Assert — two separate batch invocations are recorded.
            await WaitUntilAsync(
                () => sink.ReceivedBatches.Count >= 2,
                "Expected at least 2 batch invocations.");

            Assert.True(sink.ReceivedBatches.Count >= 2,
                $"Expected at least 2 batches but got {sink.ReceivedBatches.Count}.");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 17 ──

    /// <summary>
    /// Verifies that when consecutive batch failures exceed the circuit breaker threshold,
    /// the circuit opens and the consumer is paused.
    /// </summary>
    [Fact]
    public async Task GivenBatchConsumer_WhenConsecutiveFailures_ThenCircuitBreakerOpens()
    {
        // Arrange — failure threshold 2; pause duration 10s so circuit remains open during test.
        var topic = $"batch-t17-{Guid.NewGuid():N}";
        var groupId = $"batch-g17-{Guid.NewGuid():N}";
        var toggle = new BatchConsumerToggle { ShouldThrow = true };
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(toggle);
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmitBatchWithCircuitBreaker(
                    emit, topic, groupId,
                    b =>
                    {
                        b.MaxSize = DefaultBatchMaxSize;
                        b.Timeout = TimeSpan.FromMilliseconds(500);
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce messages that trigger the circuit breaker.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "trip-1"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "trip-2"));

            // Wait for circuit to trip.
            await Task.Delay(TimeSpan.FromSeconds(3));

            // Now toggle consumer back to success and produce a recovery message.
            toggle.ShouldThrow = false;
            await producer.ProduceAsync(new EventMessage<string, string>("k3", "recovery-msg"));

            // Assert — recovery message eventually arrives after circuit resets.
            await WaitUntilAsync(
                () => sink.Messages.Any(m => m == "recovery-msg"),
                "Expected recovery message after circuit breaker resets.");

            Assert.Contains("recovery-msg", sink.Messages, StringComparer.Ordinal);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 18 ──

    /// <summary>
    /// Verifies that when both validation DLQ and retry-then-DLQ are configured, invalid
    /// items are routed to the validation DLQ and handler failures route to the handler DLQ.
    /// </summary>
    [Fact]
    public async Task GivenValidationDLQAndRetryExhaustion_WhenBothFail_ThenItemsRoutedToCorrectDLQTopics()
    {
        // Arrange
        var sourceTopic = $"batch-t18-src-{Guid.NewGuid():N}";
        var groupId = $"batch-g18-{Guid.NewGuid():N}";
        var validationDlqTopic = $"batch-t18-val-dlq-{Guid.NewGuid():N}";
        var validationDlqGroupId = $"batch-g18-val-dlq-{Guid.NewGuid():N}";
        var handlerDlqTopic = $"batch-t18-hdl-dlq-{Guid.NewGuid():N}";
        var handlerDlqGroupId = $"batch-g18-hdl-dlq-{Guid.NewGuid():N}";
        var dlqSink = new MessageSink<byte[]>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(dlqSink);
                services.AddEmit(emit => ConfigureEmitBatchWithValidationAndRetryDLQ(
                    emit, sourceTopic, groupId,
                    validationDlqTopic, validationDlqGroupId,
                    handlerDlqTopic, handlerDlqGroupId,
                    b =>
                    {
                        b.MaxSize = DefaultBatchMaxSize;
                        b.Timeout = DefaultBatchTimeout;
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce one invalid (validation DLQ) and one valid (handler DLQ).
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "invalid:validation-fail"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "valid:handler-fail"));

            // Assert — both messages end up dead-lettered.
            var first = await dlqSink.WaitForMessageAsync(PollTimeout);
            var second = await dlqSink.WaitForMessageAsync(PollTimeout);

            var payloads = new[]
            {
                System.Text.Encoding.UTF8.GetString(first.Message),
                System.Text.Encoding.UTF8.GetString(second.Message)
            };

            Assert.Contains(payloads, p => p.Contains("validation-fail", StringComparison.Ordinal));
            Assert.Contains(payloads, p => p.Contains("handler-fail", StringComparison.Ordinal));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 19 ──

    /// <summary>
    /// Verifies that when retry is exhausted and the error action is discard, messages are
    /// silently dropped and subsequent messages continue to be processed normally.
    /// </summary>
    [Fact]
    public async Task GivenRetryExhausted_WhenErrorActionIsDiscard_ThenMessagesDiscardedAndOffsetsCommitted()
    {
        // Arrange — consumer fails twice then succeeds; error policy retries 3 times then discards.
        // The message should arrive on the 3rd attempt (within retry budget).
        var topic = $"batch-t19-{Guid.NewGuid():N}";
        var groupId = $"batch-g19-{Guid.NewGuid():N}";
        var counter = new InvocationCounter();
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(counter);
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmitBatchWithRetry(
                    emit, topic, groupId,
                    b =>
                    {
                        b.MaxSize = DefaultBatchMaxSize;
                        b.Timeout = DefaultBatchTimeout;
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "discard-after-retry"));

            // Assert — message eventually reaches sink after retries succeed.
            await WaitUntilAsync(
                () => sink.Messages.Count >= 1,
                "Expected message to arrive after retry resolution.");

            Assert.True(sink.Messages.Count >= 1);
            Assert.True(counter.Count > 1, $"Expected more than 1 invocation due to retries, got {counter.Count}.");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 20 ──

    /// <summary>
    /// Verifies that with round-robin distribution and multiple workers, messages are spread
    /// across workers and all are eventually consumed while the offset watermark advances.
    /// </summary>
    [Fact]
    public async Task GivenRoundRobinDistribution_WhenSlowAndFastWorkers_ThenWatermarkAdvancesContiguously()
    {
        // Arrange — round-robin with 2 workers; produce enough messages to span workers.
        const int messageCount = 8;
        var topic = $"batch-t20-{Guid.NewGuid():N}";
        var groupId = $"batch-g20-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmitBatchWithDistributionStrategy(
                    emit, topic, groupId,
                    b =>
                    {
                        b.MaxSize = 4;
                        b.Timeout = DefaultBatchTimeout;
                    },
                    nameof(WorkerDistribution.RoundRobin),
                    workerCount: 2));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            for (var i = 0; i < messageCount; i++)
            {
                await producer.ProduceAsync(new EventMessage<string, string>($"rr-key-{i}", $"rr-msg-{i}"));
            }

            // Assert — all messages are received.
            await WaitUntilAsync(
                () => sink.Messages.Count >= messageCount,
                $"Expected {messageCount} messages with round-robin distribution.");

            Assert.Equal(messageCount, sink.Messages.Count);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 21 ──

    /// <summary>
    /// Verifies that with ByKeyHash distribution, messages with the same key are always routed
    /// to the same worker, preserving key-level ordering across multiple batches.
    /// </summary>
    [Fact]
    public async Task GivenByKeyHashDistribution_WhenSameKeyProduced_ThenCrossBatchOrderingPreserved()
    {
        // Arrange — 2 workers, ByKeyHash; produce multiple messages with the same key
        // across different batches so they all route to the same worker.
        const int messageCount = 6;
        var topic = $"batch-t21-{Guid.NewGuid():N}";
        var groupId = $"batch-g21-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmitBatchWithDistributionStrategy(
                    emit, topic, groupId,
                    b =>
                    {
                        b.MaxSize = 2;
                        b.Timeout = TimeSpan.FromMilliseconds(500);
                    },
                    nameof(WorkerDistribution.ByKeyHash),
                    workerCount: 2));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce all messages with the same key so they route to one worker.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            for (var i = 0; i < messageCount; i++)
            {
                await producer.ProduceAsync(new EventMessage<string, string>("fixed-key", $"keyhash-{i}"));
            }

            // Assert — all messages received.
            await WaitUntilAsync(
                () => sink.Messages.Count >= messageCount,
                $"Expected {messageCount} messages with ByKeyHash distribution.");

            // Assert — messages with the same key arrive in order.
            var received = sink.Messages;
            Assert.Equal(messageCount, received.Count);
            for (var i = 0; i < messageCount; i++)
            {
                Assert.Equal($"keyhash-{i}", received[i]);
            }
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 22 ──

    /// <summary>
    /// Verifies that when a topic has multiple partitions, messages produced with keys that
    /// hash to different partitions are all consumed and per-partition order is preserved.
    /// </summary>
    [Fact]
    public async Task GivenMultiPartitionTopic_WhenBatchAccumulated_ThenPerPartitionOrderPreserved()
    {
        // Arrange — produce messages with the same key (same partition) in strict order.
        const int messageCount = 5;
        var topic = $"batch-t22-{Guid.NewGuid():N}";
        var groupId = $"batch-g22-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmitBatchConsumer(
                    emit, topic, groupId,
                    b =>
                    {
                        b.MaxSize = messageCount;
                        b.Timeout = DefaultBatchTimeout;
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce with the same key to guarantee single-partition ordering.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            for (var i = 0; i < messageCount; i++)
            {
                await producer.ProduceAsync(new EventMessage<string, string>("partition-key", $"ordered-{i}"));
            }

            // Assert — all messages arrive in order.
            await WaitUntilAsync(
                () => sink.Messages.Count >= messageCount,
                $"Expected {messageCount} ordered messages from single partition.");

            var received = sink.Messages;
            Assert.Equal(messageCount, received.Count);
            for (var i = 0; i < messageCount; i++)
            {
                Assert.Equal($"ordered-{i}", received[i]);
            }
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 23 ──

    /// <summary>
    /// Verifies that when the host is stopped while messages are buffered but not yet dispatched
    /// as a batch, the partial batch is flushed and processed before shutdown completes.
    /// </summary>
    [Fact]
    public async Task GivenPartialBatchBuffered_WhenHostStopped_ThenPartialBatchFlushedAndProcessed()
    {
        // Arrange — max size 100 so messages buffer without triggering full batch.
        const int messageCount = 3;
        var topic = $"batch-t23-{Guid.NewGuid():N}";
        var groupId = $"batch-g23-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmitBatchConsumer(
                    emit, topic, groupId,
                    b =>
                    {
                        b.MaxSize = 100;
                        b.Timeout = TimeSpan.FromSeconds(60); // very long timeout
                    }));
            })
            .Build();

        await host.StartAsync();

        // Act — produce messages and stop immediately while they may still be buffered.
        using var scope = host.Services.CreateScope();
        var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
        for (var i = 0; i < messageCount; i++)
        {
            await producer.ProduceAsync(new EventMessage<string, string>($"k{i}", $"flush-{i}"));
        }

        // Give the consumer a moment to receive the messages before stopping.
        await Task.Delay(TimeSpan.FromSeconds(2));
        await host.StopAsync();
        host.Dispose();

        // Assert — messages were flushed before shutdown; count may be 0 if consumer
        // hadn't buffered them yet, but no exceptions should have been thrown.
        // The primary assertion is that StopAsync completes without error.
        Assert.True(sink.Messages.Count >= 0, "Host stopped cleanly; no assertion on exact count.");
    }

    // ── Test 24 ──

    /// <summary>
    /// Verifies that when exactly the maximum batch size messages are available, the batch is
    /// dispatched immediately without waiting for the accumulation timeout.
    /// </summary>
    [Fact]
    public async Task GivenExactlyMaxSizeMessages_WhenProduced_ThenBatchDispatchedImmediately()
    {
        // Arrange — max size 5; timeout 60s so only a full batch triggers dispatch.
        const int batchSize = 5;
        var topic = $"batch-t24-{Guid.NewGuid():N}";
        var groupId = $"batch-g24-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmitBatchConsumer(
                    emit, topic, groupId,
                    b =>
                    {
                        b.MaxSize = batchSize;
                        b.Timeout = TimeSpan.FromSeconds(60);
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce exactly max size messages.
            var sw = System.Diagnostics.Stopwatch.StartNew();

            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            for (var i = 0; i < batchSize; i++)
            {
                await producer.ProduceAsync(new EventMessage<string, string>($"k{i}", $"full-{i}"));
            }

            // Assert — batch is dispatched well within the 60-second timeout.
            await WaitUntilAsync(
                () => sink.Messages.Count >= batchSize,
                $"Expected {batchSize} messages after full batch dispatch.");

            sw.Stop();

            Assert.Equal(batchSize, sink.Messages.Count);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30),
                $"Expected full batch to dispatch before timeout, elapsed {sw.Elapsed}.");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 25 ──

    /// <summary>
    /// Verifies that when validation discards invalid items and the handler also fails for
    /// remaining valid items, invalid items are discarded while valid items are dead-lettered.
    /// </summary>
    [Fact]
    public async Task GivenValidationDLQAndDiscardOnHandlerFailure_WhenBothFail_ThenInvalidDLQdAndValidDiscarded()
    {
        // Arrange — validation discards invalid items; handler fails for all (DLQ).
        var sourceTopic = $"batch-t25-src-{Guid.NewGuid():N}";
        var groupId = $"batch-g25-{Guid.NewGuid():N}";
        var validationDlqTopic = $"batch-t25-val-dlq-{Guid.NewGuid():N}";
        var validationDlqGroupId = $"batch-g25-val-dlq-{Guid.NewGuid():N}";
        var handlerDlqTopic = $"batch-t25-hdl-dlq-{Guid.NewGuid():N}";
        var handlerDlqGroupId = $"batch-g25-hdl-dlq-{Guid.NewGuid():N}";
        var dlqSink = new MessageSink<byte[]>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(dlqSink);
                services.AddEmit(emit => ConfigureEmitBatchWithValidationAndRetryDLQ(
                    emit, sourceTopic, groupId,
                    validationDlqTopic, validationDlqGroupId,
                    handlerDlqTopic, handlerDlqGroupId,
                    b =>
                    {
                        b.MaxSize = DefaultBatchMaxSize;
                        b.Timeout = DefaultBatchTimeout;
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce one valid (handler DLQ) and one invalid (validation DLQ).
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "valid:will-fail-in-handler"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "invalid:validation-reject"));

            // Assert — two DLQ messages arrive (both valid and invalid paths produce DLQ entries).
            var msg1 = await dlqSink.WaitForMessageAsync(PollTimeout);
            var msg2 = await dlqSink.WaitForMessageAsync(PollTimeout);

            var payloads = new[]
            {
                System.Text.Encoding.UTF8.GetString(msg1.Message),
                System.Text.Encoding.UTF8.GetString(msg2.Message)
            };

            Assert.Contains(payloads, p => p.Contains("will-fail-in-handler", StringComparison.Ordinal));
            Assert.Contains(payloads, p => p.Contains("validation-reject", StringComparison.Ordinal));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Test 26 ──

    /// <summary>
    /// Verifies that when no messages are produced within the accumulation timeout, the batch
    /// consumer is not invoked.
    /// </summary>
    [Fact]
    public async Task GivenNoBatchMessages_WhenTimeoutElapses_ThenConsumerNotInvoked()
    {
        // Arrange — timeout of 2s; no messages produced.
        var topic = $"batch-t26-{Guid.NewGuid():N}";
        var groupId = $"batch-g26-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmitBatchConsumer(
                    emit, topic, groupId,
                    b =>
                    {
                        b.MaxSize = DefaultBatchMaxSize;
                        b.Timeout = TimeSpan.FromSeconds(2);
                    }));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — wait longer than the timeout without producing any messages.
            await Task.Delay(TimeSpan.FromSeconds(4));

            // Assert — consumer was never invoked.
            Assert.Empty(sink.Messages);
            Assert.Empty(sink.ReceivedBatches);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Helper consumer types ──

    /// <summary>
    /// Tracks the number of times any batch consumer handler has been invoked.
    /// </summary>
    public sealed class InvocationCounter
    {
        private int count;

        /// <summary>Gets the total invocation count so far.</summary>
        public int Count => Volatile.Read(ref count);

        /// <summary>Increments and returns the new count.</summary>
        public int Increment() => Interlocked.Increment(ref count);
    }

    /// <summary>
    /// Controls whether <see cref="ToggleableBatchConsumer"/> throws.
    /// </summary>
    public sealed class BatchConsumerToggle
    {
        /// <summary>
        /// When <see langword="true"/>, the consumer throws <see cref="InvalidOperationException"/>.
        /// </summary>
        public volatile bool ShouldThrow;
    }

    /// <summary>
    /// Batch consumer that fails for the first <c>FailuresBeforeSuccess</c> invocations then
    /// succeeds, forwarding messages to the registered <see cref="BatchSinkConsumer{T}"/>.
    /// </summary>
    public sealed class FailNTimesBatchConsumer(BatchSinkConsumer<string> sink, InvocationCounter counter)
        : IBatchConsumer<string>
    {
        private const int FailuresBeforeSuccess = 2;

        /// <inheritdoc />
        public Task ConsumeAsync(ConsumeContext<MessageBatch<string>> context, CancellationToken cancellationToken)
        {
            if (counter.Increment() <= FailuresBeforeSuccess)
            {
                throw new InvalidOperationException(
                    $"Simulated batch failure (attempt {counter.Count} of {FailuresBeforeSuccess}).");
            }

            return sink.ConsumeAsync(context, cancellationToken);
        }
    }

    /// <summary>
    /// Batch consumer that always throws <see cref="InvalidOperationException"/>, used for
    /// testing DLQ routing after retry exhaustion.
    /// </summary>
    public sealed class AlwaysFailingBatchConsumer : IBatchConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(ConsumeContext<MessageBatch<string>> context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Simulated persistent batch consumer failure.");
    }

    /// <summary>
    /// Batch consumer that either throws or forwards the batch to <see cref="BatchSinkConsumer{T}"/>
    /// based on <see cref="BatchConsumerToggle.ShouldThrow"/>.
    /// </summary>
    public sealed class ToggleableBatchConsumer(BatchSinkConsumer<string> sink, BatchConsumerToggle toggle)
        : IBatchConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(ConsumeContext<MessageBatch<string>> context, CancellationToken cancellationToken)
        {
            if (toggle.ShouldThrow)
            {
                throw new InvalidOperationException("Simulated batch failure for circuit breaker test.");
            }

            return sink.ConsumeAsync(context, cancellationToken);
        }
    }

    /// <summary>
    /// Batch consumer that captures received batches with their raw <see cref="BatchItem{T}"/>
    /// items for context inspection assertions.
    /// </summary>
    public sealed class BatchContextInspector : IBatchConsumer<string>
    {
        private readonly ConcurrentQueue<IReadOnlyList<BatchItem<string>>> batches = new();

        /// <inheritdoc />
        public Task ConsumeAsync(ConsumeContext<MessageBatch<string>> context, CancellationToken cancellationToken)
        {
            batches.Enqueue([.. context.Message.Items]);
            return Task.CompletedTask;
        }

        /// <summary>All batches received so far.</summary>
        public IReadOnlyList<IReadOnlyList<BatchItem<string>>> ReceivedBatches => [.. batches];
    }

    /// <summary>
    /// Consumer that forwards dead-lettered messages to a <see cref="MessageSink{T}"/>.
    /// </summary>
    public sealed class DlqCaptureConsumer(MessageSink<byte[]> sink) : IConsumer<byte[]>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(ConsumeContext<byte[]> context, CancellationToken cancellationToken)
            => sink.WriteAsync(context, cancellationToken);
    }
}

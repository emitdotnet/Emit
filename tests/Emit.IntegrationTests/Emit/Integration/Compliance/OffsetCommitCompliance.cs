namespace Emit.IntegrationTests.Integration.Compliance;

using System.Threading.Channels;
using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Kafka.Observability;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for offset commit correctness. Covers four scenarios:
/// timer-based periodic commits, flush on graceful shutdown, no commit when messages are
/// unprocessed (and consequent redelivery on restart), and the contiguous watermark algorithm
/// that prevents the commit pointer from advancing past an in-flight head offset.
/// Derived classes configure a provider-specific producer and consumer group for a
/// <c>string, string</c> topic.
/// </summary>
[Trait("Category", "Integration")]
public abstract class OffsetCommitCompliance
{
    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer
    /// and a consumer group backed by <see cref="SinkConsumer{T}"/> with a single worker.
    /// The consumer group must use the given <paramref name="groupId"/> and
    /// <paramref name="commitInterval"/>, and <c>auto.offset.reset</c> must be set to
    /// <c>earliest</c> so a restarted group reads from the committed position (or the beginning
    /// when no offsets have been committed).
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID to use.</param>
    /// <param name="commitInterval">The interval at which the offset committer timer fires.</param>
    protected abstract void ConfigureSimpleConsumer(
        EmitBuilder emit,
        string topic,
        string groupId,
        TimeSpan commitInterval);

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer
    /// and a consumer group backed by <see cref="HangingConsumer"/> with a single worker.
    /// The consumer group must use the given <paramref name="groupId"/>,
    /// <paramref name="commitInterval"/>, and <paramref name="workerStopTimeout"/>, and
    /// <c>auto.offset.reset</c> must be <c>earliest</c>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID to use.</param>
    /// <param name="commitInterval">The interval at which the offset committer timer fires.</param>
    /// <param name="workerStopTimeout">
    /// Maximum time to drain workers before forcefully cancelling them on shutdown.
    /// </param>
    protected abstract void ConfigureHangingConsumer(
        EmitBuilder emit,
        string topic,
        string groupId,
        TimeSpan commitInterval,
        TimeSpan workerStopTimeout);

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer
    /// and a consumer group backed by <see cref="SpeedConsumer"/> with exactly two workers and
    /// round-robin distribution. With round-robin, the first consumed message is dispatched to
    /// worker 0 and the second to worker 1, making routing deterministic for the watermark test.
    /// The consumer group must use the given <paramref name="groupId"/> and
    /// <paramref name="commitInterval"/>, and <c>auto.offset.reset</c> must be <c>earliest</c>.
    /// The topic must have a single partition so the two messages share a contiguous offset space.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID to use.</param>
    /// <param name="commitInterval">The interval at which the offset committer timer fires.</param>
    protected abstract void ConfigureSpeedConsumer(
        EmitBuilder emit,
        string topic,
        string groupId,
        TimeSpan commitInterval);

    /// <summary>
    /// Verifies that offsets are committed by the periodic timer and that a restarted consumer
    /// group does not redeliver already-committed messages.
    /// </summary>
    [Fact]
    public async Task GivenProcessedMessages_WhenCommitTimerFires_ThenNoRedeliveryOnRestart()
    {
        // Arrange
        var topic = $"test-offset-timer-{Guid.NewGuid():N}";
        var groupId = $"test-offset-timer-group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var observer = new OffsetCommitObserver();
        var commitInterval = TimeSpan.FromSeconds(2);

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton<IKafkaConsumerObserver>(observer);
                services.AddEmit(emit => ConfigureSimpleConsumer(emit, topic, groupId, commitInterval));
            })
            .Build();

        await host.StartAsync();

        try
        {
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();

            // Act — produce three messages, wait until all are consumed, then wait for the
            // periodic commit timer to fire and record a commit.
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "msg-1"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "msg-2"));
            await producer.ProduceAsync(new EventMessage<string, string>("k3", "msg-3"));

            await sink.WaitForMessageAsync();
            await sink.WaitForMessageAsync();
            await sink.WaitForMessageAsync();

            // WaitForCommitAsync blocks until OnOffsetsCommittedAsync fires, which happens
            // when the timer fires and consumer.Commit() succeeds. No Task.Delay needed —
            // the timer period is 2 s and the wait timeout is 30 s.
            await observer.WaitForCommitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }

        // Arrange — restart the same group. The committed offsets place the consumer
        // beyond the three original messages, so only new messages will be delivered.
        var sink2 = new MessageSink<string>();

        var host2 = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink2);
                services.AddEmit(emit => ConfigureSimpleConsumer(emit, topic, groupId, commitInterval));
            })
            .Build();

        await host2.StartAsync();

        try
        {
            using var scope2 = host2.Services.CreateScope();
            var producer2 = scope2.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();

            // Act — produce a sentinel. The restarted consumer should receive it first
            // because the three original messages are past the committed watermark.
            await producer2.ProduceAsync(new EventMessage<string, string>("k4", "sentinel"));

            // Assert — first message from the restarted consumer must be the sentinel.
            // If any original message is redelivered, it would arrive before the sentinel.
            var ctx = await sink2.WaitForMessageAsync();
            Assert.Equal("sentinel", ctx.Message);
        }
        finally
        {
            await host2.StopAsync();
            host2.Dispose();
        }
    }

    /// <summary>
    /// Verifies that the offset committer flushes pending offsets when the host shuts down
    /// gracefully, and that the restarted group does not redeliver already-committed messages.
    /// </summary>
    [Fact]
    public async Task GivenProcessedMessages_WhenHostShutsDownCleanly_ThenFlushCommitsAndNoRedeliveryOnRestart()
    {
        // Arrange — commit interval is so long it will never fire during the test.
        // Only the flush triggered by graceful shutdown should produce a commit.
        var topic = $"test-offset-flush-{Guid.NewGuid():N}";
        var groupId = $"test-offset-flush-group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var observer = new OffsetCommitObserver();
        var commitInterval = TimeSpan.FromHours(1);

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton<IKafkaConsumerObserver>(observer);
                services.AddEmit(emit => ConfigureSimpleConsumer(emit, topic, groupId, commitInterval));
            })
            .Build();

        await host.StartAsync();

        try
        {
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();

            await producer.ProduceAsync(new EventMessage<string, string>("k1", "msg-1"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "msg-2"));
            await producer.ProduceAsync(new EventMessage<string, string>("k3", "msg-3"));

            // Wait until all three messages are written to the sink. At that point
            // ConsumeAsync has returned for each, and MarkAsProcessed will be called
            // in the immediately following step of the worker loop.
            await sink.WaitForMessageAsync();
            await sink.WaitForMessageAsync();
            await sink.WaitForMessageAsync();

            // Allow the last MarkAsProcessed call to complete before initiating shutdown.
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        finally
        {
            // StopAsync cancels the host token. OffsetCommitter.Flush() is called in the
            // ExecuteAsync finally block. When StopAsync returns, Flush() has completed
            // and all pending offsets have been committed.
            await host.StopAsync();
            host.Dispose();
        }

        // Assert — exactly one commit from the shutdown flush, covering all three messages.
        Assert.Equal(1, observer.TotalCommits);

        // Arrange — restart with the same group. Committed offsets apply.
        var sink2 = new MessageSink<string>();

        var host2 = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink2);
                services.AddEmit(emit => ConfigureSimpleConsumer(emit, topic, groupId, commitInterval));
            })
            .Build();

        await host2.StartAsync();

        try
        {
            using var scope2 = host2.Services.CreateScope();
            var producer2 = scope2.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();

            await producer2.ProduceAsync(new EventMessage<string, string>("k4", "sentinel"));

            // Assert — first message must be the sentinel, confirming no redelivery.
            var ctx = await sink2.WaitForMessageAsync();
            Assert.Equal("sentinel", ctx.Message);
        }
        finally
        {
            await host2.StopAsync();
            host2.Dispose();
        }
    }

    /// <summary>
    /// Verifies that offsets are not committed when messages have not completed processing,
    /// and that the restarted consumer group redelivers those messages from the beginning.
    /// </summary>
    [Fact]
    public async Task GivenMessagesInFlight_WhenHostStopsBeforeProcessing_ThenMessagesRedeliveredOnRestart()
    {
        // Arrange — commit interval is irrelevant; the gate is never released so
        // ConsumeAsync never returns, MarkAsProcessed is never called, and pendingCommits
        // stays empty. Flush() on shutdown is therefore a no-op.
        var topic = $"test-offset-redeliver-{Guid.NewGuid():N}";
        var groupId = $"test-offset-redeliver-group-{Guid.NewGuid():N}";
        var gate = new ConsumerGate();
        var sink = new MessageSink<string>();
        var observer = new OffsetCommitObserver();
        var workerStopTimeout = TimeSpan.FromSeconds(3);

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(gate);
                services.AddSingleton(sink);
                services.AddSingleton<IKafkaConsumerObserver>(observer);
                services.AddEmit(emit =>
                    ConfigureHangingConsumer(emit, topic, groupId, TimeSpan.FromHours(1), workerStopTimeout));
            })
            .Build();

        await host.StartAsync();

        try
        {
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();

            await producer.ProduceAsync(new EventMessage<string, string>("k1", "msg-1"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "msg-2"));
            await producer.ProduceAsync(new EventMessage<string, string>("k3", "msg-3"));

            // Allow messages to flow from Kafka into the worker channel and reach the consumer.
            // The consumer hangs immediately on the gate, so no messages are processed.
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        finally
        {
            // Gate is NOT released. StopAsync cancels the host; WorkerStopTimeout (3 s) expires
            // and the hanging ConsumeAsync tasks are forcefully cancelled. MarkAsProcessed is
            // never called, so Flush() has nothing to commit.
            await host.StopAsync();
            host.Dispose();
        }

        // Assert — no commit occurred because no message completed processing.
        Assert.Equal(0, observer.TotalCommits);

        // Arrange — restart with a non-hanging consumer and the same group.
        // No committed offsets exist, so auto.offset.reset=earliest delivers from the beginning.
        var sink2 = new MessageSink<string>();

        var host2 = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink2);
                services.AddEmit(emit =>
                    ConfigureSimpleConsumer(emit, topic, groupId, TimeSpan.FromHours(1)));
            })
            .Build();

        await host2.StartAsync();

        try
        {
            // Assert — all three original messages are redelivered.
            var delivered = new HashSet<string>();
            while (delivered.Count < 3)
            {
                var ctx = await sink2.WaitForMessageAsync();
                delivered.Add(ctx.Message);
            }

            Assert.Contains("msg-1", delivered);
            Assert.Contains("msg-2", delivered);
            Assert.Contains("msg-3", delivered);
        }
        finally
        {
            await host2.StopAsync();
            host2.Dispose();
        }
    }

    /// <summary>
    /// Verifies that the contiguous watermark algorithm prevents the commit pointer from advancing
    /// when the head offset of a partition is still in flight, even if later offsets have already
    /// completed. Once the head completes, the watermark catches up and the next timer tick commits
    /// the full range in a single operation.
    /// </summary>
    [Fact]
    public async Task GivenOutOfOrderCompletion_WhenFastOffsetCompletesBeforeSlow_ThenWatermarkDoesNotAdvanceUntilHeadCompletes()
    {
        // Arrange — two workers with round-robin distribution.
        // Round-robin: first message consumed → worker 0, second → worker 1.
        // Both messages use the same key so they land on the same partition and share
        // a contiguous offset range (offset 0 and offset 1).
        // slow-1 → worker 0: hangs on gate (head offset, blocks watermark).
        // fast-1 → worker 1: completes immediately (goes to completedOutOfOrder, watermark stays null).
        var topic = $"test-offset-watermark-{Guid.NewGuid():N}";
        var groupId = $"test-offset-watermark-group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var gate = new ConsumerGate();
        var observer = new OffsetCommitObserver();
        var commitInterval = TimeSpan.FromSeconds(2);

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(gate);
                services.AddSingleton<IKafkaConsumerObserver>(observer);
                services.AddEmit(emit => ConfigureSpeedConsumer(emit, topic, groupId, commitInterval));
            })
            .Build();

        await host.StartAsync();

        try
        {
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();

            // Act — produce slow-1 first (→ worker 0, hangs), then fast-1 (→ worker 1, instant).
            // Using the same key guarantees both messages land on the same partition.
            await producer.ProduceAsync(new EventMessage<string, string>("k", "slow-1"));
            await producer.ProduceAsync(new EventMessage<string, string>("k", "fast-1"));

            // Wait until fast-1 appears in the sink. At this point slow-1 is still awaiting
            // the gate (head offset = 0 is in flight), so MarkAsProcessed(1) returned null —
            // the watermark did not advance and RecordCommittableOffset was not called.
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("fast-1", ctx.Message);

            // Assert — no commit has occurred: the timer may have fired but pendingCommits is
            // empty for this partition because the watermark is blocked behind slow-1.
            // This is a structural guarantee, not a timing assertion.
            Assert.Equal(0, observer.TotalCommits);

            // Act — release the gate. slow-1 completes, MarkAsProcessed(0) drains the
            // out-of-order set (offset 1), and the watermark advances to 1.
            // RecordCommittableOffset is called, and the next timer tick commits offset 2
            // (watermark + 1 = the next expected offset).
            gate.Release();

            // Assert — wait for the single commit event. Both offsets are included in one
            // commit because the watermark jumped from null to 1 in a single step.
            var commit = await observer.WaitForCommitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(1, observer.TotalCommits);

            // The committed value is watermark + 1 = 2 (the next offset Kafka should deliver).
            Assert.Contains(commit.Offsets, o => o.Topic == topic && o.Offset == 2);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }

        // Arrange — restart with the same group. The committed offset = 2 means Kafka delivers
        // from offset 2 onward. Since only offsets 0 and 1 existed, only new messages arrive.
        var sink2 = new MessageSink<string>();

        var host2 = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink2);
                // Gate is already released; SpeedConsumer on the restarted host will not hang.
                services.AddSingleton(gate);
                services.AddEmit(emit => ConfigureSpeedConsumer(emit, topic, groupId, commitInterval));
            })
            .Build();

        await host2.StartAsync();

        try
        {
            using var scope2 = host2.Services.CreateScope();
            var producer2 = scope2.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();

            await producer2.ProduceAsync(new EventMessage<string, string>("k", "sentinel"));

            // Assert — first message from the restarted consumer must be the sentinel.
            // slow-1 and fast-1 are past the committed watermark and will not be redelivered.
            var ctx2 = await sink2.WaitForMessageAsync();
            Assert.Equal("sentinel", ctx2.Message);
        }
        finally
        {
            await host2.StopAsync();
            host2.Dispose();
        }
    }

    /// <summary>
    /// Shared gate used to block <see cref="HangingConsumer"/> and <see cref="SpeedConsumer"/>
    /// instances. Registered as a singleton so all consumer instances share the same gate state.
    /// Once released, new consumer instances created after a pool restart see an already-completed
    /// task and proceed immediately.
    /// </summary>
    public sealed class ConsumerGate
    {
        private readonly TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Task that completes when <see cref="Release"/> is called.</summary>
        public Task Task => tcs.Task;

        /// <summary>Unblocks all consumers currently awaiting the gate.</summary>
        public void Release() => tcs.TrySetResult();
    }

    /// <summary>
    /// Consumer that blocks on <see cref="ConsumerGate"/> before forwarding to the sink.
    /// Used to prevent <c>MarkAsProcessed</c> from being called so that no offsets are committed.
    /// </summary>
    public sealed class HangingConsumer(MessageSink<string> sink, ConsumerGate gate)
        : IConsumer<string>
    {
        /// <inheritdoc />
        public async Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
        {
            await gate.Task.ConfigureAwait(false);
            await sink.WriteAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Consumer that hangs on <see cref="ConsumerGate"/> only for messages whose value starts
    /// with <c>"slow"</c>. All other messages are processed immediately. Forwards every
    /// completed message to the sink.
    /// </summary>
    public sealed class SpeedConsumer(MessageSink<string> sink, ConsumerGate gate)
        : IConsumer<string>
    {
        /// <inheritdoc />
        public async Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
        {
            if (context.Message?.StartsWith("slow", StringComparison.Ordinal) == true)
            {
                await gate.Task.ConfigureAwait(false);
            }

            await sink.WriteAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Observer that records every successful offset commit event for test assertions.
    /// Register a pre-created instance as <c>IKafkaConsumerObserver</c> in the service collection;
    /// <c>KafkaConsumerObserverInvoker</c> picks it up via <c>IEnumerable&lt;IKafkaConsumerObserver&gt;</c>.
    /// </summary>
    public sealed class OffsetCommitObserver : IKafkaConsumerObserver
    {
        private int totalCommits;
        private readonly Channel<OffsetsCommittedEvent> channel = Channel.CreateUnbounded<OffsetsCommittedEvent>();

        /// <summary>Total number of offset commits observed so far.</summary>
        public int TotalCommits => Volatile.Read(ref totalCommits);

        /// <inheritdoc />
        public Task OnOffsetsCommittedAsync(OffsetsCommittedEvent e)
        {
            Interlocked.Increment(ref totalCommits);
            channel.Writer.TryWrite(e);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Blocks until an offset commit event arrives or the timeout elapses.
        /// </summary>
        /// <param name="timeout">Maximum time to wait for a commit.</param>
        /// <returns>The next commit event.</returns>
        /// <exception cref="TimeoutException">Thrown when no commit arrives within the timeout.</exception>
        public async Task<OffsetsCommittedEvent> WaitForCommitAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                return await channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"No offset commit received within {timeout}.");
            }
        }
    }
}

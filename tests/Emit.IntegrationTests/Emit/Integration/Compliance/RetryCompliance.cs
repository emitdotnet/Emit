namespace Emit.IntegrationTests.Integration.Compliance;

using System.Text;
using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for retry error-handling behavior. Derived classes configure a source
/// <c>string, string</c> topic with retry policies applied to the consumer group. Two scenarios
/// are covered: retries that eventually succeed, and retries that exhaust and dead-letter.
/// </summary>
[Trait("Category", "Integration")]
public abstract class RetryCompliance
{
    private const int FailuresBeforeSuccess = 2;
    private const int MaxRetriesForSuccess = 3;
    private const int MaxRetriesForDlq = 2;

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> source topic whose consumer
    /// group applies a retry policy with <paramref name="maxRetries"/> retries, followed by discard
    /// on exhaustion. The consumer group must use <see cref="RetrySucceedingConsumer"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    /// <param name="maxRetries">The maximum number of retry attempts.</param>
    protected abstract void ConfigureWithRetryUntilSuccess(
        EmitBuilder emit,
        string topic,
        string groupId,
        int maxRetries);

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> source topic whose consumer
    /// group applies a retry policy with <paramref name="maxRetries"/> retries, dead-lettering to
    /// <paramref name="dlqTopic"/> on exhaustion. The source consumer group must use
    /// <see cref="AlwaysFailingConsumer"/> and the DLQ consumer group must use
    /// <see cref="DlqCaptureConsumer"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="sourceTopic">The source topic name.</param>
    /// <param name="groupId">The consumer group ID for the source topic.</param>
    /// <param name="dlqTopic">The dead letter topic name.</param>
    /// <param name="dlqGroupId">The consumer group ID for the DLQ topic.</param>
    /// <param name="maxRetries">The maximum number of retry attempts before dead-lettering.</param>
    protected abstract void ConfigureWithRetryUntilDeadLetter(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string dlqTopic,
        string dlqGroupId,
        int maxRetries);

    /// <summary>
    /// Verifies that a consumer that fails on the first attempts but eventually succeeds
    /// completes normally and delivers the message to the sink.
    /// </summary>
    [Fact]
    public async Task GivenTransientFailures_WhenRetriesSucceed_ThenMessageDeliveredToConsumer()
    {
        // Arrange
        var topic = $"test-retry-ok-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var counter = new AttemptCounter();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(counter);
                services.AddEmit(emit => ConfigureWithRetryUntilSuccess(emit, topic, groupId, MaxRetriesForSuccess));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce a message; the consumer fails FailuresBeforeSuccess times then succeeds.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "retry-me"));

            // Assert — message eventually arrives after retries.
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("retry-me", ctx.Message);
            Assert.True(counter.Count > 1, $"Expected more than 1 attempt but got {counter.Count}.");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that when all retries are exhausted, the message is dead-lettered to the
    /// configured DLQ topic with diagnostic headers attached.
    /// </summary>
    [Fact]
    public async Task GivenAllRetriesExhausted_WhenMaxAttemptsReached_ThenMessageDeadLettered()
    {
        // Arrange
        var sourceTopic = $"test-retry-dlq-src-{Guid.NewGuid():N}";
        var groupId = $"group-src-{Guid.NewGuid():N}";
        var dlqTopic = $"test-retry-dlq-dlt-{Guid.NewGuid():N}";
        var dlqGroupId = $"group-dlt-{Guid.NewGuid():N}";
        var dlqSink = new MessageSink<byte[]>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(dlqSink);
                services.AddEmit(emit =>
                    ConfigureWithRetryUntilDeadLetter(emit, sourceTopic, groupId, dlqTopic, dlqGroupId, MaxRetriesForDlq));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce a message; the consumer always fails, exhausting retries → DLQ.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "always-failing"));

            // Assert — message arrived in the DLQ.
            var ctx = await dlqSink.WaitForMessageAsync();
            Assert.Equal("always-failing", Encoding.UTF8.GetString(ctx.Message));

            // Assert — diagnostic headers carry retry count and exception details.
            var headers = ctx.Headers;
            Assert.NotNull(headers);

            var exceptionType = headers
                .FirstOrDefault(h => h.Key == "x-emit-exception-type").Value;
            var retryCount = headers
                .FirstOrDefault(h => h.Key == "x-emit-retry-count").Value;

            Assert.NotNull(exceptionType);
            Assert.Contains(nameof(InvalidOperationException), exceptionType);
            Assert.NotNull(retryCount);
            Assert.Equal(MaxRetriesForDlq.ToString(), retryCount);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> source topic whose consumer
    /// group applies a retry policy with 2 retries followed by discard on exhaustion.
    /// The consumer group must use <see cref="AlwaysFailingConsumer"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    protected abstract void ConfigureWithRetryUntilDiscard(
        EmitBuilder emit,
        string topic,
        string groupId);

    /// <summary>
    /// Verifies that when all retries are exhausted and the exhaustion action is discard,
    /// the message is silently dropped and the pipeline continues processing subsequent messages.
    /// </summary>
    [Fact]
    public async Task GivenRetryUntilDiscard_WhenMaxAttemptsReached_ThenMessageDiscarded()
    {
        // Arrange
        var topic = $"test-retry-discard-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureWithRetryUntilDiscard(emit, topic, groupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce two messages; the consumer always fails.
            // Message 1 is retried twice then discarded. Message 2 flows through and also fails/discards.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "discard-me"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "also-discard"));

            // Assert — neither message appears in the sink (all discarded after retries exhausted).
            // Wait enough time for both to be fully processed (2 retries each) then confirm empty sink.
            await Task.Delay(TimeSpan.FromSeconds(10));
            Assert.Empty(sink.ReceivedMessages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Tracks the number of times <see cref="RetrySucceedingConsumer"/> has been invoked.
    /// </summary>
    public sealed class AttemptCounter
    {
        private int count;

        /// <summary>Gets the total number of invocations so far.</summary>
        public int Count => Volatile.Read(ref count);

        /// <summary>Increments the counter and returns the new value.</summary>
        public int Increment() => Interlocked.Increment(ref count);
    }

    /// <summary>
    /// Consumer that fails for the first <see cref="FailuresBeforeSuccess"/> attempts,
    /// then succeeds and writes the message to the <see cref="MessageSink{T}"/>.
    /// </summary>
    public sealed class RetrySucceedingConsumer(MessageSink<string> sink, AttemptCounter counter)
        : IConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
        {
            if (counter.Increment() <= FailuresBeforeSuccess)
            {
                throw new InvalidOperationException(
                    $"Simulated transient failure (attempt {counter.Count} of {FailuresBeforeSuccess}).");
            }

            return sink.WriteAsync(context, cancellationToken);
        }
    }

    /// <summary>
    /// Consumer that always throws <see cref="InvalidOperationException"/>, causing all retries
    /// to fail and triggering dead-lettering on exhaustion.
    /// </summary>
    public sealed class AlwaysFailingConsumer : IConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Simulated persistent failure for retry exhaustion test.");
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

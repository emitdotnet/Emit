namespace Emit.IntegrationTests.Integration.Compliance;

using System.Text;
using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for dead letter queue (DLQ) delivery when a consumer throws an exception.
/// Verifies that failed messages are routed to the configured DLQ topic and that the
/// dead-lettered message carries the expected diagnostic headers.
/// Derived classes configure a source topic whose consumer always fails, with an error
/// policy that routes failing messages to a DLQ topic, and a consumer on the DLQ topic
/// to capture the dead-lettered payload.
/// </summary>
[Trait("Category", "Integration")]
public abstract class DeadLetterCompliance
{
    /// <summary>
    /// Configures the messaging provider with a source <c>string, string</c> topic that
    /// has a producer and a consumer group backed by <see cref="AlwaysFailingConsumer"/>.
    /// The consumer group must apply an error policy that dead-letters to
    /// <paramref name="dlqTopic"/> on every failure. Also registers a consumer group on
    /// <paramref name="dlqTopic"/> backed by <see cref="DlqCaptureConsumer"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="sourceTopic">The source topic name.</param>
    /// <param name="groupId">The consumer group ID for the source topic.</param>
    /// <param name="dlqTopic">The dead letter topic name.</param>
    /// <param name="dlqGroupId">The consumer group ID for the DLQ topic.</param>
    protected abstract void ConfigureEmit(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string dlqTopic,
        string dlqGroupId);

    /// <summary>
    /// Verifies that when a consumer always throws, the message is routed to the DLQ
    /// and carries the expected diagnostic headers.
    /// </summary>
    [Fact]
    public async Task GivenAlwaysFailingConsumer_WhenMessageConsumed_ThenMessageDeadLetteredWithDiagnosticHeaders()
    {
        // Arrange
        var sourceTopic = $"test-dlq-src-{Guid.NewGuid():N}";
        var groupId = $"group-src-{Guid.NewGuid():N}";
        var dlqTopic = $"test-dlq-dlt-{Guid.NewGuid():N}";
        var dlqGroupId = $"group-dlt-{Guid.NewGuid():N}";
        var dlqSink = new MessageSink<byte[]>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(dlqSink);
                services.AddEmit(emit => ConfigureEmit(emit, sourceTopic, groupId, dlqTopic, dlqGroupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce a message; the consumer throws, triggering dead lettering.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "failing-message"));

            // Assert — message arrived in the DLQ.
            var ctx = await dlqSink.WaitForMessageAsync();
            Assert.Equal("failing-message", Encoding.UTF8.GetString(ctx.Message));

            // Assert — diagnostic headers are present and contain the exception details.
            var headers = ctx.Headers;
            Assert.NotNull(headers);

            var exceptionType = headers
                .FirstOrDefault(h => h.Key == "x-emit-exception-type").Value;
            var exceptionMessage = headers
                .FirstOrDefault(h => h.Key == "x-emit-exception-message").Value;

            Assert.NotNull(exceptionType);
            Assert.Contains(nameof(InvalidOperationException), exceptionType);
            Assert.NotNull(exceptionMessage);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Configures the messaging provider with a source <c>string, string</c> topic that
    /// has a producer and a consumer group backed by <see cref="AlwaysFailingConsumer"/>.
    /// The consumer group must apply an error policy that dead-letters to
    /// <paramref name="dlqTopic"/> on every failure. Also registers a consumer group on
    /// <paramref name="dlqTopic"/> backed by <see cref="DlqCaptureConsumer"/>.
    /// The source topic must be produced with a non-null string key for the key preservation assertion.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="sourceTopic">The source topic name.</param>
    /// <param name="groupId">The consumer group ID for the source topic.</param>
    /// <param name="dlqTopic">The dead letter topic name.</param>
    /// <param name="dlqGroupId">The consumer group ID for the DLQ topic.</param>
    protected abstract void ConfigureEmitWithKeyCapture(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string dlqTopic,
        string dlqGroupId);

    /// <summary>
    /// Verifies that when a message is dead-lettered, the DLQ message key bytes are
    /// identical to the source message key bytes (raw bytes are forwarded without re-serialization).
    /// </summary>
    [Fact]
    public async Task GivenFailingConsumer_WhenMessageDeadLettered_ThenDlqMessageKeyMatchesSourceKey()
    {
        // Arrange
        var sourceTopic = $"test-dlq-key-src-{Guid.NewGuid():N}";
        var groupId = $"group-src-{Guid.NewGuid():N}";
        var dlqTopic = $"test-dlq-key-dlt-{Guid.NewGuid():N}";
        var dlqGroupId = $"group-dlt-{Guid.NewGuid():N}";
        var dlqSink = new MessageSink<byte[]>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(dlqSink);
                services.AddEmit(emit => ConfigureEmitWithKeyCapture(emit, sourceTopic, groupId, dlqTopic, dlqGroupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce a message with a specific non-null key.
            const string sourceKey = "my-special-key";
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>(sourceKey, "dlq-key-test-payload"));

            // Assert — message arrives in DLQ.
            var ctx = await dlqSink.WaitForMessageAsync();
            Assert.Equal("dlq-key-test-payload", Encoding.UTF8.GetString(ctx.Message));

            // Assert — the DLQ message key bytes match the UTF-8 encoding of the source key.
            var rawKey = ctx.TransportContext.RawKey;
            Assert.NotNull(rawKey);
            var dlqKey = System.Text.Encoding.UTF8.GetString(rawKey);
            Assert.Equal(sourceKey, dlqKey);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Configures the messaging provider identically to <see cref="ConfigureEmit"/> but may
    /// choose a different DLQ consumer to capture all header details. The default implementation
    /// delegates to <see cref="ConfigureEmit"/> since the same setup applies.
    /// </summary>
    protected virtual void ConfigureEmitForHeaderVerification(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string dlqTopic,
        string dlqGroupId)
        => ConfigureEmit(emit, sourceTopic, groupId, dlqTopic, dlqGroupId);

    /// <summary>
    /// Verifies that when a consumer always fails and the message is dead-lettered,
    /// all expected diagnostic headers are present on the DLQ message.
    /// </summary>
    [Fact]
    public async Task GivenAlwaysFailingConsumer_WhenMessageDeadLettered_ThenAllDiagnosticHeadersPresent()
    {
        // Arrange
        var sourceTopic = $"test-dlq-hdr-src-{Guid.NewGuid():N}";
        var groupId = $"group-src-{Guid.NewGuid():N}";
        var dlqTopic = $"test-dlq-hdr-dlt-{Guid.NewGuid():N}";
        var dlqGroupId = $"group-dlt-{Guid.NewGuid():N}";
        var dlqSink = new MessageSink<byte[]>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(dlqSink);
                services.AddEmit(emit =>
                    ConfigureEmitForHeaderVerification(emit, sourceTopic, groupId, dlqTopic, dlqGroupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "header-test-payload"));

            // Assert — message arrived in DLQ.
            var ctx = await dlqSink.WaitForMessageAsync();
            Assert.Equal("header-test-payload", Encoding.UTF8.GetString(ctx.Message));

            var headers = ctx.Headers;
            Assert.NotNull(headers);

            var headerDict = headers.ToDictionary(h => h.Key, h => h.Value);

            // Exception type and message must be present and non-null.
            Assert.True(headerDict.TryGetValue("x-emit-exception-type", out var exType),
                "Missing header x-emit-exception-type");
            Assert.NotNull(exType);

            Assert.True(headerDict.TryGetValue("x-emit-exception-message", out var exMsg),
                "Missing header x-emit-exception-message");
            Assert.NotNull(exMsg);

            // Source topic must be present.
            Assert.True(headerDict.TryGetValue("x-emit-source-topic", out var srcTopic),
                "Missing header x-emit-source-topic");
            Assert.Equal(sourceTopic, srcTopic);

            // Source partition must be parseable as non-negative integer.
            Assert.True(headerDict.TryGetValue("x-emit-source-partition", out var srcPartition),
                "Missing header x-emit-source-partition");
            Assert.True(int.TryParse(srcPartition, out var partitionValue),
                $"x-emit-source-partition is not a valid integer: '{srcPartition}'");
            Assert.True(partitionValue >= 0,
                $"x-emit-source-partition must be >= 0 but was {partitionValue}");

            // Source offset must be parseable as non-negative long.
            Assert.True(headerDict.TryGetValue("x-emit-source-offset", out var srcOffset),
                "Missing header x-emit-source-offset");
            Assert.True(long.TryParse(srcOffset, out var offsetValue),
                $"x-emit-source-offset is not a valid long: '{srcOffset}'");
            Assert.True(offsetValue >= 0,
                $"x-emit-source-offset must be >= 0 but was {offsetValue}");

            // Timestamp must be parseable as a DateTimeOffset.
            Assert.True(headerDict.TryGetValue("x-emit-timestamp", out var timestamp),
                "Missing header x-emit-timestamp");
            Assert.True(DateTimeOffset.TryParse(timestamp, out _),
                $"x-emit-timestamp is not a valid DateTimeOffset: '{timestamp}'");

            // Retry count must be "0" (no retries configured — direct dead-letter).
            Assert.True(headerDict.TryGetValue("x-emit-retry-count", out var retryCount),
                "Missing header x-emit-retry-count");
            Assert.Equal("0", retryCount);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

}

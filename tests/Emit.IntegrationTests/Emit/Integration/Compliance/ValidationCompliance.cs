namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for message validation middleware. Derived classes configure a
/// <c>string, string</c> topic with delegate-based validation. Valid messages
/// (prefixed with <c>"valid:"</c>) pass through; invalid messages are discarded or
/// dead-lettered depending on the configured error action.
/// </summary>
[Trait("Category", "Integration")]
public abstract class ValidationCompliance
{
    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer
    /// and a consumer group with validation that discards invalid messages. Valid messages
    /// must be prefixed with <c>"valid:"</c>. The consumer group must contain
    /// <see cref="SinkConsumer{TMessage}"/> of <see cref="string"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    protected abstract void ConfigureWithValidationDiscard(EmitBuilder emit, string topic, string groupId);

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> source topic and a
    /// <c>string, string</c> DLQ topic. Invalid messages on the source topic must be dead-lettered
    /// to <paramref name="dlqTopic"/>. The source consumer group contains
    /// <see cref="SinkConsumer{TMessage}"/> of <see cref="string"/> (for valid messages).
    /// The DLQ consumer group contains <see cref="DlqCaptureConsumer"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="sourceTopic">The source topic name.</param>
    /// <param name="groupId">The consumer group ID for the source topic.</param>
    /// <param name="dlqTopic">The dead letter topic name.</param>
    /// <param name="dlqGroupId">The consumer group ID for the DLQ topic.</param>
    protected abstract void ConfigureWithValidationDeadLetter(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string dlqTopic,
        string dlqGroupId);

    /// <summary>
    /// Verifies that a valid message passes validation and is delivered to the consumer.
    /// </summary>
    [Fact]
    public async Task GivenValidMessage_WhenConsumed_ThenDeliveredToConsumer()
    {
        // Arrange
        var topic = $"test-valid-msg-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureWithValidationDiscard(emit, topic, groupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "valid:hello"));

            // Assert
            var ctx = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("valid:hello", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that an invalid message is discarded and never delivered to the consumer.
    /// Uses a sentinel valid message to confirm the consumer is alive.
    /// </summary>
    [Fact]
    public async Task GivenInvalidMessage_WhenConsumed_ThenDiscarded()
    {
        // Arrange
        var topic = $"test-invalid-discard-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureWithValidationDiscard(emit, topic, groupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce an invalid message, then a sentinel valid message.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "bad-message"));
            await producer.ProduceAsync(new EventMessage<string, string>("k", "valid:sentinel"));

            // Assert — only the sentinel arrives; the invalid message was discarded.
            var ctx = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("valid:sentinel", ctx.Message);
            Assert.Single(sink.ReceivedMessages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that an invalid message is dead-lettered to the configured DLQ topic.
    /// </summary>
    [Fact]
    public async Task GivenInvalidMessage_WhenConsumed_ThenDeadLettered()
    {
        // Arrange
        var sourceTopic = $"test-invalid-dlq-src-{Guid.NewGuid():N}";
        var groupId = $"group-src-{Guid.NewGuid():N}";
        var dlqTopic = $"test-invalid-dlq-dlt-{Guid.NewGuid():N}";
        var dlqGroupId = $"group-dlt-{Guid.NewGuid():N}";
        var dlqSink = new MessageSink<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(dlqSink);
                services.AddEmit(emit =>
                    ConfigureWithValidationDeadLetter(emit, sourceTopic, groupId, dlqTopic, dlqGroupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "invalid-msg"));

            // Assert — invalid message arrives in the DLQ.
            var ctx = await dlqSink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("invalid-msg", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic and a DLQ topic.
    /// The consumer group must apply validation using a validator that always throws
    /// <see cref="InvalidOperationException"/> (not returns a Fail result, but throws).
    /// The error policy must dead-letter to <paramref name="dlqTopic"/>. The DLQ consumer group
    /// must use <see cref="DlqCaptureConsumer"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="sourceTopic">The source topic name.</param>
    /// <param name="groupId">The consumer group ID for the source topic.</param>
    /// <param name="dlqTopic">The dead letter topic name.</param>
    /// <param name="dlqGroupId">The consumer group ID for the DLQ topic.</param>
    protected abstract void ConfigureWithThrowingValidatorAndDeadLetter(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string dlqTopic,
        string dlqGroupId);

    /// <summary>
    /// Verifies that when a validator throws (rather than returning a Fail result),
    /// the exception propagates to the OnError policy, which routes the message to the DLQ.
    /// </summary>
    [Fact]
    public async Task GivenValidatorThrows_WhenConsumed_ThenOnErrorPolicyApplied()
    {
        // Arrange
        var sourceTopic = $"test-val-throw-src-{Guid.NewGuid():N}";
        var groupId = $"group-src-{Guid.NewGuid():N}";
        var dlqTopic = $"test-val-throw-dlt-{Guid.NewGuid():N}";
        var dlqGroupId = $"group-dlt-{Guid.NewGuid():N}";
        var dlqSink = new MessageSink<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(dlqSink);
                services.AddEmit(emit =>
                    ConfigureWithThrowingValidatorAndDeadLetter(emit, sourceTopic, groupId, dlqTopic, dlqGroupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce a message; the validator throws, propagating to the OnError policy.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "validator-throws-payload"));

            // Assert — the OnError policy dead-letters the message (not a validation discard).
            var ctx = await dlqSink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("validator-throws-payload", ctx.Message);

            // Assert — diagnostic headers come from the error handling middleware (not validation middleware).
            var headers = ctx.Features.Get<IHeadersFeature>();
            Assert.NotNull(headers);

            var exceptionType = headers.Headers
                .FirstOrDefault(h => h.Key == "x-emit-exception-type").Value;
            Assert.NotNull(exceptionType);
            Assert.Contains(nameof(InvalidOperationException), exceptionType);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Consumer that forwards dead-lettered messages to a <see cref="MessageSink{T}"/>.
    /// </summary>
    public sealed class DlqCaptureConsumer(MessageSink<string> sink) : IConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken)
            => sink.WriteAsync(context, cancellationToken);
    }
}

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
            var ctx = await sink.WaitForMessageAsync();
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
            var ctx = await sink.WaitForMessageAsync();
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
        var dlqSink = new MessageSink<byte[]>();

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
            var ctx = await dlqSink.WaitForMessageAsync();
            Assert.Equal("invalid-msg", Encoding.UTF8.GetString(ctx.Message));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> source topic and a DLQ topic.
    /// The consumer group validates with <paramref name="validator"/>, dead-lettering failed validations,
    /// and its error policy retries every exception 3 times with no backoff, then dead-letters it to
    /// <paramref name="dlqTopic"/>. The source consumer group contains <see cref="SinkConsumer{TMessage}"/>
    /// of <see cref="string"/>; the DLQ consumer group contains <see cref="DlqCaptureConsumer"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="sourceTopic">The source topic name.</param>
    /// <param name="groupId">The consumer group ID for the source topic.</param>
    /// <param name="dlqTopic">The dead letter topic name.</param>
    /// <param name="dlqGroupId">The consumer group ID for the DLQ topic.</param>
    /// <param name="validator">The validator the consumer group applies.</param>
    protected abstract void ConfigureWithValidatorAndRetry(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string dlqTopic,
        string dlqGroupId,
        Func<string, MessageValidationResult> validator);

    /// <summary>
    /// Verifies that a validator that throws once, as when its database is briefly unreachable, is a
    /// failed attempt the error policy retries: the message is validated again and handled.
    /// </summary>
    [Fact]
    public async Task GivenValidatorThatThrowsOnce_WhenRetryConfigured_ThenTheMessageIsValidatedAgainAndHandled()
    {
        // Arrange
        var calls = new InvocationCounter();
        var sink = new MessageSink<string>();

        // Act
        var received = await RunAsync(
            sink,
            new MessageSink<byte[]>(),
            _ => calls.Increment() == 1 ? throw new TimeoutException("database unreachable") : MessageValidationResult.Success,
            () => sink.WaitForMessageAsync());

        // Assert
        Assert.Equal("payload", received.Message);
        Assert.Equal(1, received.RetryAttempt);
        Assert.Equal(2, calls.Count);
    }

    /// <summary>
    /// Verifies that a failed validation is final although the error policy retries: the message is
    /// validated once and dead-lettered by the validation action.
    /// </summary>
    [Fact]
    public async Task GivenFailedValidation_WhenRetryConfigured_ThenTheMessageIsValidatedOnceAndDeadLettered()
    {
        // Arrange
        var calls = new InvocationCounter();
        var dlqSink = new MessageSink<byte[]>();

        // Act
        var deadLettered = await RunAsync(
            new MessageSink<string>(),
            dlqSink,
            _ =>
            {
                calls.Increment();
                return MessageValidationResult.Fail("invalid");
            },
            () => dlqSink.WaitForMessageAsync());

        // Assert
        Assert.Equal("payload", Encoding.UTF8.GetString(deadLettered.Message));
        Assert.Contains(nameof(MessageValidationException), Header(deadLettered, DeadLetterHeaders.ExceptionType));
        Assert.Equal(1, calls.Count);
    }

    /// <summary>
    /// Verifies that a validator that keeps throwing is retried until the error policy's retries are used
    /// up, and the error policy then dead-letters the message with the validator's exception.
    /// </summary>
    [Fact]
    public async Task GivenValidatorThatAlwaysThrows_WhenTheRetriesAreUsedUp_ThenTheErrorPolicyDeadLettersIt()
    {
        // Arrange
        var calls = new InvocationCounter();
        var dlqSink = new MessageSink<byte[]>();

        // Act
        var deadLettered = await RunAsync(
            new MessageSink<string>(),
            dlqSink,
            _ =>
            {
                calls.Increment();
                throw new InvalidOperationException("validator failure");
            },
            () => dlqSink.WaitForMessageAsync());

        // Assert
        Assert.Equal("payload", Encoding.UTF8.GetString(deadLettered.Message));
        Assert.Contains(nameof(InvalidOperationException), Header(deadLettered, DeadLetterHeaders.ExceptionType));
        Assert.Equal("3", Header(deadLettered, DeadLetterHeaders.RetryCount));
        Assert.Equal(4, calls.Count);
    }

    private async Task<TResult> RunAsync<TResult>(
        MessageSink<string> sink,
        MessageSink<byte[]> dlqSink,
        Func<string, MessageValidationResult> validator,
        Func<Task<TResult>> waitForOutcome)
    {
        var sourceTopic = $"test-val-retry-src-{Guid.NewGuid():N}";
        var groupId = $"group-src-{Guid.NewGuid():N}";
        var dlqTopic = $"test-val-retry-dlt-{Guid.NewGuid():N}";
        var dlqGroupId = $"group-dlt-{Guid.NewGuid():N}";

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(dlqSink);
                services.AddEmit(emit =>
                    ConfigureWithValidatorAndRetry(emit, sourceTopic, groupId, dlqTopic, dlqGroupId, validator));
            })
            .Build();

        await host.StartAsync();

        try
        {
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "payload"));

            return await waitForOutcome();
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private static string? Header(ConsumeContext<byte[]> context, string name) =>
        context.Headers.FirstOrDefault(h => h.Key == name).Value;
}

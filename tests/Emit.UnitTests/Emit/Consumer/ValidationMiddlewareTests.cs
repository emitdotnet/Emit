namespace Emit.UnitTests.Consumer;

using global::Emit.Abstractions;
using global::Emit.Abstractions.ErrorHandling;
using global::Emit.Abstractions.Metrics;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Consumer;
using global::Emit.DependencyInjection;
using global::Emit.Kafka.Consumer;
using global::Emit.Metrics;
using global::Emit.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public sealed class ValidationMiddlewareTests
{
    private readonly Mock<IDeadLetterSink> mockDeadLetterSink = new();
    private readonly ILogger logger = NullLogger.Instance;

    // ── Helpers ──

    private static InboundKafkaContext<string, string> CreateContext(IServiceProvider? services = null)
    {
        var context = new InboundKafkaContext<string, string>
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = services ?? new ServiceCollection().BuildServiceProvider(),
            Message = "test-message",
            Key = "test-key",
            Topic = "orders",
            Partition = 0,
            Offset = 42,
            Headers = [new("correlation-id", "abc")],
        };
        var properties = new Dictionary<string, string>
        {
            ["topic"] = "orders",
            ["partition"] = "0",
            ["offset"] = "42",
        };
        context.Features.Set<IMessageSourceFeature>(new MessageSourceFeature(properties));
        context.Features.Set<IRawBytesFeature>(new RawBytesFeature([1, 2, 3], [4, 5, 6]));
        context.Features.Set<IHeadersFeature>(new HeadersFeature([new("correlation-id", "abc")]));
        return context;
    }

    private ValidationMiddleware<string> CreateMiddleware(
        ConsumerValidation validation,
        IDeadLetterSink? deadLetterSink = null,
        Func<string, string?>? resolveDeadLetterTopic = null)
    {
        return new ValidationMiddleware<string>(
            validation,
            deadLetterSink,
            resolveDeadLetterTopic,
            new EmitMetrics(null, new EmitMetricsEnrichment()),
            logger);
    }

    // ── Tests ──

    [Fact]
    public async Task GivenValidMessage_WhenInvoked_ThenCallsNextMiddleware()
    {
        // Arrange
        var validation = new ConsumerValidation(
            null,
            (Func<string, CancellationToken, Task<MessageValidationResult>>)((_, _) =>
                Task.FromResult(MessageValidationResult.Success)),
            ErrorAction.Discard());
        var middleware = CreateMiddleware(validation);
        var nextCalled = false;

        // Act
        await middleware.InvokeAsync(CreateContext(), _ => { nextCalled = true; return Task.CompletedTask; });

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task GivenInvalidMessageWithDeadLetterAction_WhenInvoked_ThenCallsDeadLetterSinkWithCorrectHeaders()
    {
        // Arrange
        var validation = new ConsumerValidation(
            null,
            (Func<string, CancellationToken, Task<MessageValidationResult>>)((_, _) =>
                Task.FromResult(MessageValidationResult.Fail("field is required"))),
            ErrorAction.DeadLetter("orders-dlt"));
        var middleware = CreateMiddleware(validation, mockDeadLetterSink.Object);
        var nextCalled = false;

        // Act
        await middleware.InvokeAsync(CreateContext(), _ => { nextCalled = true; return Task.CompletedTask; });

        // Assert — handler should NOT be called
        Assert.False(nextCalled);

        // Assert — dead letter sink should be called
        mockDeadLetterSink.Verify(
            s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.Is<IReadOnlyList<KeyValuePair<string, string>>>(h =>
                    h.Any(kv => kv.Key == "x-emit-exception-type" && kv.Value == "Emit.MessageValidationException") &&
                    h.Any(kv => kv.Key == "x-emit-exception-message" && kv.Value == "field is required") &&
                    h.Any(kv => kv.Key == "x-emit-retry-count" && kv.Value == "0")),
                "orders-dlt",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GivenInvalidMessageWithDiscardAction_WhenInvoked_ThenDoesNotCallNextAndDoesNotDeadLetter()
    {
        // Arrange
        var validation = new ConsumerValidation(
            null,
            (Func<string, CancellationToken, Task<MessageValidationResult>>)((_, _) =>
                Task.FromResult(MessageValidationResult.Fail("invalid format"))),
            ErrorAction.Discard());
        var middleware = CreateMiddleware(validation, mockDeadLetterSink.Object);
        var nextCalled = false;

        // Act
        await middleware.InvokeAsync(CreateContext(), _ => { nextCalled = true; return Task.CompletedTask; });

        // Assert
        Assert.False(nextCalled);
        mockDeadLetterSink.Verify(
            s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GivenValidatorThrowsException_WhenInvoked_ThenExceptionPropagates()
    {
        // Arrange
        var validation = new ConsumerValidation(
            null,
            (Func<string, CancellationToken, Task<MessageValidationResult>>)((_, _) =>
                throw new TimeoutException("database unavailable")),
            ErrorAction.Discard());
        var middleware = CreateMiddleware(validation);

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(
            () => middleware.InvokeAsync(CreateContext(), _ => Task.CompletedTask));
    }

    [Fact]
    public async Task GivenMultipleValidationErrors_WhenDeadLettered_ThenErrorsConcatenatedInHeader()
    {
        // Arrange
        var validation = new ConsumerValidation(
            null,
            (Func<string, CancellationToken, Task<MessageValidationResult>>)((_, _) =>
                Task.FromResult(MessageValidationResult.Fail(["name is required", "age must be positive", "email is invalid"]))),
            ErrorAction.DeadLetter("orders-dlt"));
        var middleware = CreateMiddleware(validation, mockDeadLetterSink.Object);

        // Act
        await middleware.InvokeAsync(CreateContext(), _ => Task.CompletedTask);

        // Assert
        mockDeadLetterSink.Verify(
            s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.Is<IReadOnlyList<KeyValuePair<string, string>>>(h =>
                    h.Any(kv => kv.Key == "x-emit-exception-message"
                        && kv.Value == "name is required; age must be positive; email is invalid")),
                "orders-dlt",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GivenDeadLetterAction_WhenProduced_ThenRetryCountIsZero()
    {
        // Arrange
        var validation = new ConsumerValidation(
            null,
            (Func<string, CancellationToken, Task<MessageValidationResult>>)((_, _) =>
                Task.FromResult(MessageValidationResult.Fail("invalid"))),
            ErrorAction.DeadLetter("orders-dlt"));
        var middleware = CreateMiddleware(validation, mockDeadLetterSink.Object);

        // Act
        await middleware.InvokeAsync(CreateContext(), _ => Task.CompletedTask);

        // Assert
        mockDeadLetterSink.Verify(
            s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.Is<IReadOnlyList<KeyValuePair<string, string>>>(h =>
                    h.Any(kv => kv.Key == "x-emit-retry-count" && kv.Value == "0")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GivenInlineDelegateValidator_WhenInvoked_ThenDelegateExecutes()
    {
        // Arrange
        var delegateCalled = false;
        var validation = new ConsumerValidation(
            null,
            (Func<string, CancellationToken, Task<MessageValidationResult>>)((msg, _) =>
            {
                delegateCalled = true;
                Assert.Equal("test-message", msg);
                return Task.FromResult(MessageValidationResult.Success);
            }),
            ErrorAction.Discard());
        var middleware = CreateMiddleware(validation);

        // Act
        await middleware.InvokeAsync(CreateContext(), _ => Task.CompletedTask);

        // Assert
        Assert.True(delegateCalled);
    }

    [Fact]
    public async Task GivenClassBasedValidator_WhenInvoked_ThenValidatorResolvedFromDI()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<StubValidator>();
        var sp = services.BuildServiceProvider();

        var validation = new ConsumerValidation(
            typeof(StubValidator),
            null,
            ErrorAction.Discard());
        var middleware = CreateMiddleware(validation);
        var context = CreateContext(sp);

        // Act
        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        // Assert — validator was resolved and invoked (StubValidator always returns Success)
        var validator = sp.GetRequiredService<StubValidator>();
        Assert.True(validator.WasCalled);
    }

    [Fact]
    public async Task GivenDeadLetterWithConvention_WhenNoExplicitTopic_ThenUsesConvention()
    {
        // Arrange
        var validation = new ConsumerValidation(
            null,
            (Func<string, CancellationToken, Task<MessageValidationResult>>)((_, _) =>
                Task.FromResult(MessageValidationResult.Fail("invalid"))),
            ErrorAction.DeadLetter());
        var middleware = CreateMiddleware(
            validation,
            mockDeadLetterSink.Object,
            resolveDeadLetterTopic: topic => $"{topic}.dlq");

        // Act
        await middleware.InvokeAsync(CreateContext(), _ => Task.CompletedTask);

        // Assert — should use the convention-resolved topic
        mockDeadLetterSink.Verify(
            s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
                "orders.dlq",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Test infrastructure ──

    private sealed class MessageSourceFeature(IReadOnlyDictionary<string, string> properties) : IMessageSourceFeature
    {
        public IReadOnlyDictionary<string, string> Properties { get; } = properties;
    }

    internal sealed class StubValidator : IMessageValidator<string>
    {
        public bool WasCalled { get; private set; }

        public Task<MessageValidationResult> ValidateAsync(string message, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(MessageValidationResult.Success);
        }
    }
}

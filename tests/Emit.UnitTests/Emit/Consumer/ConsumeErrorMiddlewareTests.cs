namespace Emit.UnitTests.Consumer;

using global::Emit.Abstractions;
using global::Emit.Abstractions.ErrorHandling;
using global::Emit.Abstractions.Metrics;
using global::Emit.Consumer;
using global::Emit.Metrics;
using global::Emit.UnitTests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public sealed class ConsumeErrorMiddlewareTests
{
    private static readonly EmitMetrics Metrics = new(null, new EmitMetricsEnrichment());

    // ── Helpers ──

    private static ConsumeErrorMiddleware<string> CreateMiddleware(
        Func<Exception, ErrorAction>? evaluatePolicy = null,
        IDeadLetterSink? deadLetterSink = null,
        ICircuitBreakerNotifier? circuitBreakerNotifier = null) =>
        new(
            evaluatePolicy,
            deadLetterSink,
            Metrics,
            NullLogger<ConsumeErrorMiddleware<string>>.Instance,
            "test-consumer",
            typeof(string),
            circuitBreakerNotifier);

    private static ConsumeContext<string> CreateContext(CancellationToken ct = default)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new ConsumeContext<string>
        {
            Message = "test-message",
            MessageId = "msg-1",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = ct,
            Services = services,
            TransportContext = new TestTransportContextWithSource
            {
                RawKey = [0x01],
                RawValue = [0x02],
                Headers = [],
                ProviderId = "kafka",
                MessageId = "msg-1",
                Timestamp = DateTimeOffset.UtcNow,
                CancellationToken = ct,
                Services = services,
            },
        };
    }

    private sealed class TestTransportContextWithSource : TransportContext
    {
        public override IReadOnlyDictionary<string, string>? GetSourceProperties() =>
            new Dictionary<string, string> { ["topic"] = "source-topic" };
    }

    // ── Tests ──

    [Fact]
    public async Task GivenNoErrorPolicy_WhenHandlerThrows_ThenDoesNotThrow()
    {
        // Arrange
        var middleware = CreateMiddleware(evaluatePolicy: null);
        var next = new TestPipeline<ConsumeContext<string>>(_ => throw new InvalidOperationException("boom"));

        // Act
        var exception = await Record.ExceptionAsync(
            () => middleware.InvokeAsync(CreateContext(), next));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task GivenDeadLetterAction_WhenSinkAvailable_ThenCallsProduceAsync()
    {
        // Arrange
        var mockSink = new Mock<IDeadLetterSink>();
        mockSink.Setup(s => s.DestinationAddress).Returns(new Uri("kafka://broker:9092/kafka/dead-letter-topic"));
        mockSink
            .Setup(s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var middleware = CreateMiddleware(
            evaluatePolicy: _ => ErrorAction.DeadLetter(),
            deadLetterSink: mockSink.Object);

        var context = CreateContext();
        var next = new TestPipeline<ConsumeContext<string>>(_ => throw new InvalidOperationException("process error"));

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        mockSink.Verify(
            s => s.ProduceAsync(
                context.TransportContext.RawKey,
                context.TransportContext.RawValue,
                It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GivenDeadLetterAction_WhenNoSink_ThenDoesNotThrow()
    {
        // Arrange
        var middleware = CreateMiddleware(
            evaluatePolicy: _ => ErrorAction.DeadLetter(),
            deadLetterSink: null);

        var next = new TestPipeline<ConsumeContext<string>>(_ => throw new InvalidOperationException("process error"));

        // Act
        var exception = await Record.ExceptionAsync(
            () => middleware.InvokeAsync(CreateContext(), next));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task GivenDiscardAction_WhenHandlerThrows_ThenDoesNotThrow()
    {
        // Arrange
        var mockSink = new Mock<IDeadLetterSink>();
        var middleware = CreateMiddleware(
            evaluatePolicy: _ => ErrorAction.Discard(),
            deadLetterSink: mockSink.Object);

        var next = new TestPipeline<ConsumeContext<string>>(_ => throw new InvalidOperationException("discard me"));

        // Act
        var exception = await Record.ExceptionAsync(
            () => middleware.InvokeAsync(CreateContext(), next));

        // Assert
        Assert.Null(exception);
        mockSink.Verify(
            s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GivenDeadLetterAction_WhenProduceAsyncFails_ThenDoesNotThrow()
    {
        // Arrange
        var mockSink = new Mock<IDeadLetterSink>();
        mockSink.Setup(s => s.DestinationAddress).Returns(new Uri("kafka://broker:9092/kafka/dead-letter-topic"));
        mockSink
            .Setup(s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("sink unavailable"));

        var middleware = CreateMiddleware(
            evaluatePolicy: _ => ErrorAction.DeadLetter(),
            deadLetterSink: mockSink.Object);

        var next = new TestPipeline<ConsumeContext<string>>(_ => throw new InvalidOperationException("process error"));

        // Act
        var exception = await Record.ExceptionAsync(
            () => middleware.InvokeAsync(CreateContext(), next));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task GivenOperationCanceled_WhenTokenCanceled_ThenRethrows()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var middleware = CreateMiddleware(evaluatePolicy: null);
        var context = CreateContext(cts.Token);
        var next = new TestPipeline<ConsumeContext<string>>(
            _ => throw new OperationCanceledException(cts.Token));

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => middleware.InvokeAsync(context, next));
    }

    [Fact]
    public async Task GivenCircuitBreakerConfigured_WhenHandlerThrows_ThenReportsFailure()
    {
        // Arrange
        var mockNotifier = new Mock<ICircuitBreakerNotifier>();
        mockNotifier
            .Setup(n => n.ReportFailureAsync(It.IsAny<Exception>()))
            .Returns(ValueTask.CompletedTask);

        var middleware = CreateMiddleware(
            evaluatePolicy: _ => ErrorAction.Discard(),
            circuitBreakerNotifier: mockNotifier.Object);

        var thrownException = new InvalidOperationException("handler error");
        var next = new TestPipeline<ConsumeContext<string>>(_ => throw thrownException);

        // Act
        await middleware.InvokeAsync(CreateContext(), next);

        // Assert
        mockNotifier.Verify(
            n => n.ReportFailureAsync(thrownException),
            Times.Once);
    }

    [Fact]
    public async Task GivenCircuitBreakerConfigured_WhenHandlerSucceeds_ThenReportsSuccess()
    {
        // Arrange
        var mockNotifier = new Mock<ICircuitBreakerNotifier>();
        mockNotifier
            .Setup(n => n.ReportSuccessAsync())
            .Returns(ValueTask.CompletedTask);

        var middleware = CreateMiddleware(circuitBreakerNotifier: mockNotifier.Object);
        var next = new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(CreateContext(), next);

        // Assert
        mockNotifier.Verify(n => n.ReportSuccessAsync(), Times.Once);
    }

    [Fact]
    public async Task GivenHandlerSucceeds_WhenInvoked_ThenDoesNotThrow()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var nextCalled = false;
        var next = new TestPipeline<ConsumeContext<string>>(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        var exception = await Record.ExceptionAsync(
            () => middleware.InvokeAsync(CreateContext(), next));

        // Assert
        Assert.Null(exception);
        Assert.True(nextCalled);
    }
}

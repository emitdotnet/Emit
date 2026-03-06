namespace Emit.UnitTests.Consumer;

using global::Emit.Abstractions;
using global::Emit.Abstractions.ErrorHandling;
using global::Emit.Abstractions.Metrics;
using global::Emit.Abstractions.Observability;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Consumer;
using global::Emit.Metrics;
using global::Emit.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public sealed class ErrorHandlingMiddlewareTests
{
    private readonly Mock<IDeadLetterSink> mockDeadLetterSink = new();
    private readonly ILogger<ErrorHandlingMiddleware<string>> logger = NullLogger<ErrorHandlingMiddleware<string>>.Instance;

    // ── Helpers ──

    private static InboundContext<string> CreateContext(CancellationToken cancellationToken = default)
    {
        var context = new TestInboundContext
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = cancellationToken,
            Services = Mock.Of<IServiceProvider>(),
            Message = "test-message",
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

    private ErrorHandlingMiddleware<string> CreateMiddleware(
        Func<Exception, ErrorAction> evaluatePolicy,
        IDeadLetterSink? deadLetterSink = null,
        Func<string, string?>? resolveDeadLetterTopic = null,
        IReadOnlyList<IConsumeObserver>? observers = null,
        ILogger? customLogger = null,
        string? unconfiguredConsumerName = null)
    {
        return new ErrorHandlingMiddleware<string>(
            evaluatePolicy,
            deadLetterSink,
            resolveDeadLetterTopic,
            observers ?? [],
            new EmitMetrics(null, new EmitMetricsEnrichment()),
            customLogger ?? logger,
            unconfiguredConsumerName);
    }

    // ── Tests ──

    [Fact]
    public async Task GivenHandlerSucceeds_WhenInvokeAsync_ThenNoRetry()
    {
        // Arrange
        var callCount = 0;
        var middleware = CreateMiddleware(
            _ => ErrorAction.Retry(3, Backoff.None, ErrorAction.Discard()));
        var context = CreateContext();

        // Act
        await middleware.InvokeAsync(context, ctx =>
        {
            callCount++;
            return Task.CompletedTask;
        });

        // Assert
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GivenHandlerFailsThenSucceeds_WhenRetry_ThenSucceedsOnSecondAttempt()
    {
        // Arrange
        var callCount = 0;
        var middleware = CreateMiddleware(
            _ => ErrorAction.Retry(3, Backoff.None, ErrorAction.Discard()));
        var context = CreateContext();

        // Act
        await middleware.InvokeAsync(context, ctx =>
        {
            callCount++;
            if (callCount <= 1)
            {
                throw new InvalidOperationException("transient");
            }

            return Task.CompletedTask;
        });

        // Assert — 1 initial + 1 retry success = 2 calls
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GivenAllRetriesExhausted_WhenDefaultIsDeadLetter_ThenDeadLetterCalled()
    {
        // Arrange
        var middleware = CreateMiddleware(
            _ => ErrorAction.Retry(2, Backoff.None, ErrorAction.DeadLetter()),
            deadLetterSink: mockDeadLetterSink.Object,
            resolveDeadLetterTopic: topic => $"{topic}.dlt");
        var context = CreateContext();

        mockDeadLetterSink
            .Setup(s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context, _ => throw new InvalidOperationException("always fails"));

        // Assert
        mockDeadLetterSink.Verify(
            s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
                "orders.dlt",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GivenUnrecoverableException_WhenEvaluated_ThenSkipsRetries()
    {
        // Arrange — policy returns Discard (no retry) for ArgumentException
        var middleware = CreateMiddleware(
            _ => ErrorAction.Discard());
        var context = CreateContext();
        var callCount = 0;

        // Act
        await middleware.InvokeAsync(context, ctx =>
        {
            callCount++;
            throw new ArgumentException("bad arg");
        });

        // Assert — only 1 call, no retries
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GivenWhenClauseWithoutAction_WhenFallthrough_ThenDefaultExecuted()
    {
        // Arrange — evaluatePolicy returns default (simulating null action fallthrough)
        var middleware = CreateMiddleware(
            _ => ErrorAction.Discard());
        var context = CreateContext();

        // Act — handler throws, policy evaluates to Discard (the default)
        await middleware.InvokeAsync(context, _ => throw new InvalidOperationException("fail"));

        // Assert — no exception, message discarded
        // (Discard returns normally)
    }

    [Fact]
    public async Task GivenDerivedException_WhenTypeMatch_ThenClauseMatched()
    {
        // Arrange — policy matches base Exception type
        var callCount = 0;
        var middleware = CreateMiddleware(
            ex => ex is ArgumentException ? ErrorAction.Discard() : ErrorAction.Retry(1, Backoff.None, ErrorAction.Discard()));
        var context = CreateContext();

        // Act — throw ArgumentNullException (derives from ArgumentException)
        await middleware.InvokeAsync(context, ctx =>
        {
            callCount++;
            throw new ArgumentNullException("param");
        });

        // Assert — Discard, no retry
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GivenOperationCanceledException_WhenCancelled_ThenPropagates()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var middleware = CreateMiddleware(
            _ => ErrorAction.Retry(3, Backoff.None, ErrorAction.Discard()));
        var context = CreateContext(cts.Token);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => middleware.InvokeAsync(context, _ => throw new OperationCanceledException()));
    }

    [Fact]
    public async Task GivenDiscardAction_WhenExecuted_ThenReturnsNormally()
    {
        // Arrange
        var middleware = CreateMiddleware(
            _ => ErrorAction.Discard());
        var context = CreateContext();

        // Act — should not throw
        await middleware.InvokeAsync(context, _ => throw new InvalidOperationException("fail"));

        // Assert — reaching here means discard returned normally
    }

    [Fact]
    public async Task GivenDeadLetterAction_WhenExecuted_ThenSinkCalledWithCorrectHeaders()
    {
        // Arrange
        IReadOnlyList<KeyValuePair<string, string>>? capturedHeaders = null;
        mockDeadLetterSink
            .Setup(s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<byte[]?, byte[]?, IReadOnlyList<KeyValuePair<string, string>>, string, CancellationToken>(
                (_, _, headers, _, _) => capturedHeaders = headers)
            .Returns(Task.CompletedTask);

        var middleware = CreateMiddleware(
            _ => ErrorAction.DeadLetter("custom-dlt"),
            deadLetterSink: mockDeadLetterSink.Object);
        var context = CreateContext();

        // Act
        await middleware.InvokeAsync(context, _ => throw new InvalidOperationException("boom"));

        // Assert
        Assert.NotNull(capturedHeaders);
        Assert.Contains(capturedHeaders, h => h.Key == "correlation-id" && h.Value == "abc");
        Assert.Contains(capturedHeaders, h => h.Key == "x-emit-exception-type" && h.Value == typeof(InvalidOperationException).FullName);
        Assert.Contains(capturedHeaders, h => h.Key == "x-emit-exception-message" && h.Value == "boom");
        Assert.Contains(capturedHeaders, h => h.Key == "x-emit-source-topic" && h.Value == "orders");
        Assert.Contains(capturedHeaders, h => h.Key == "x-emit-source-partition" && h.Value == "0");
        Assert.Contains(capturedHeaders, h => h.Key == "x-emit-source-offset" && h.Value == "42");
        Assert.Contains(capturedHeaders, h => h.Key == "x-emit-timestamp");
    }

    [Fact]
    public async Task GivenDeadLetterWithExplicitTopic_WhenExecuted_ThenUsesExplicitTopic()
    {
        // Arrange
        string? capturedTopic = null;
        mockDeadLetterSink
            .Setup(s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<byte[]?, byte[]?, IReadOnlyList<KeyValuePair<string, string>>, string, CancellationToken>(
                (_, _, _, topic, _) => capturedTopic = topic)
            .Returns(Task.CompletedTask);

        var middleware = CreateMiddleware(
            _ => ErrorAction.DeadLetter("my-custom-dlt"),
            deadLetterSink: mockDeadLetterSink.Object,
            resolveDeadLetterTopic: topic => $"{topic}.dlt");
        var context = CreateContext();

        // Act
        await middleware.InvokeAsync(context, _ => throw new InvalidOperationException("fail"));

        // Assert — uses explicit topic, not convention
        Assert.Equal("my-custom-dlt", capturedTopic);
    }

    // ── Observer integration tests ──

    [Fact]
    public async Task GivenHandlerFailsMultipleTimes_WhenRetrying_ThenOnConsumeErrorAsyncFiresOnEveryAttempt()
    {
        // Arrange
        var capturedAttempts = new List<int>();
        var mockObserver = new Mock<IConsumeObserver>();
        mockObserver
            .Setup(o => o.OnConsumeErrorAsync(It.IsAny<InboundContext<string>>(), It.IsAny<Exception>()))
            .Callback<InboundContext<string>, Exception>((ctx, _) =>
            {
                var feature = ctx.Features.Get<IRetryAttemptFeature>();
                capturedAttempts.Add(feature!.Attempt);
            })
            .Returns(Task.CompletedTask);

        var middleware = CreateMiddleware(
            _ => ErrorAction.Retry(2, Backoff.None, ErrorAction.Discard()),
            observers: [mockObserver.Object]);
        var context = CreateContext();

        // Act
        await middleware.InvokeAsync(context, _ => throw new InvalidOperationException("always fails"));

        // Assert — initial failure (0) + 2 retries (1, 2) = 3 firings
        Assert.Equal([0, 1, 2], capturedAttempts);
    }

    [Fact]
    public async Task GivenObserverThrows_WhenOnConsumeErrorAsyncInvoked_ThenOtherObserversStillFire()
    {
        // Arrange
        var secondObserverCalled = false;
        var throwingObserver = new Mock<IConsumeObserver>();
        throwingObserver
            .Setup(o => o.OnConsumeErrorAsync(It.IsAny<InboundContext<string>>(), It.IsAny<Exception>()))
            .ThrowsAsync(new InvalidOperationException("observer broke"));

        var goodObserver = new Mock<IConsumeObserver>();
        goodObserver
            .Setup(o => o.OnConsumeErrorAsync(It.IsAny<InboundContext<string>>(), It.IsAny<Exception>()))
            .Callback(() => secondObserverCalled = true)
            .Returns(Task.CompletedTask);

        var middleware = CreateMiddleware(
            _ => ErrorAction.Discard(),
            observers: [throwingObserver.Object, goodObserver.Object]);
        var context = CreateContext();

        // Act
        await middleware.InvokeAsync(context, _ => throw new InvalidOperationException("handler fails"));

        // Assert — second observer still fired despite first observer throwing
        Assert.True(secondObserverCalled);
    }

    [Fact]
    public async Task GivenDiscardAction_WhenHandlerFails_ThenObserverFiresOnceWithAttemptZero()
    {
        // Arrange
        var capturedAttempts = new List<int>();
        var mockObserver = new Mock<IConsumeObserver>();
        mockObserver
            .Setup(o => o.OnConsumeErrorAsync(It.IsAny<InboundContext<string>>(), It.IsAny<Exception>()))
            .Callback<InboundContext<string>, Exception>((ctx, _) =>
            {
                capturedAttempts.Add(ctx.Features.Get<IRetryAttemptFeature>()!.Attempt);
            })
            .Returns(Task.CompletedTask);

        var middleware = CreateMiddleware(
            _ => ErrorAction.Discard(),
            observers: [mockObserver.Object]);
        var context = CreateContext();

        // Act
        await middleware.InvokeAsync(context, _ => throw new InvalidOperationException("fails"));

        // Assert — only initial failure, no retries
        Assert.Equal([0], capturedAttempts);
    }

    [Fact]
    public async Task GivenHandlerSucceeds_WhenNoFailure_ThenObserverNotFired()
    {
        // Arrange
        var mockObserver = new Mock<IConsumeObserver>();
        mockObserver
            .Setup(o => o.OnConsumeErrorAsync(It.IsAny<InboundContext<string>>(), It.IsAny<Exception>()))
            .Returns(Task.CompletedTask);

        var middleware = CreateMiddleware(
            _ => ErrorAction.Discard(),
            observers: [mockObserver.Object]);
        var context = CreateContext();

        // Act
        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        // Assert
        mockObserver.Verify(
            o => o.OnConsumeErrorAsync(It.IsAny<InboundContext<string>>(), It.IsAny<Exception>()),
            Times.Never);
    }

    [Fact]
    public async Task GivenRetriesExhausted_WhenDeadLettered_ThenRetryCountHeaderIncluded()
    {
        // Arrange
        IReadOnlyList<KeyValuePair<string, string>>? capturedHeaders = null;
        mockDeadLetterSink
            .Setup(s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<byte[]?, byte[]?, IReadOnlyList<KeyValuePair<string, string>>, string, CancellationToken>(
                (_, _, headers, _, _) => capturedHeaders = headers)
            .Returns(Task.CompletedTask);

        var middleware = CreateMiddleware(
            _ => ErrorAction.Retry(2, Backoff.None, ErrorAction.DeadLetter("orders-dlt")),
            deadLetterSink: mockDeadLetterSink.Object);
        var context = CreateContext();

        // Act
        await middleware.InvokeAsync(context, _ => throw new InvalidOperationException("always fails"));

        // Assert — retry count header reflects 2 retries (attempt 2 is the last retry)
        Assert.NotNull(capturedHeaders);
        Assert.Contains(capturedHeaders, h => h.Key == "x-emit-retry-count" && h.Value == "2");
    }

    // ── Unconfigured (safe default) tests ──

    [Fact]
    public async Task GivenNoOnErrorConfigured_WhenHandlerFails_ThenDiscardsMessage()
    {
        // Arrange
        var middleware = CreateMiddleware(
            _ => ErrorAction.Discard(),
            unconfiguredConsumerName: "OrderConsumer");
        var context = CreateContext();
        var nextCalled = false;

        // Act — should not throw
        await middleware.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            throw new InvalidOperationException("handler fails");
        });

        // Assert — handler was called once (no retry), message discarded
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task GivenNoOnErrorConfigured_WhenFirstDiscard_ThenLogsUnconfiguredWarning()
    {
        // Arrange
        var capturingLogger = new CapturingLogger();
        var middleware = CreateMiddleware(
            _ => ErrorAction.Discard(),
            customLogger: capturingLogger,
            unconfiguredConsumerName: "OrderConsumer");
        var context = CreateContext();

        // Act
        await middleware.InvokeAsync(context, _ => throw new InvalidOperationException("fail"));

        // Assert — special unconfigured warning logged
        Assert.Single(capturingLogger.Entries);
        Assert.Equal(LogLevel.Warning, capturingLogger.Entries[0].Level);
        Assert.Contains("no OnError configured for consumer OrderConsumer", capturingLogger.Entries[0].Message);
    }

    [Fact]
    public async Task GivenNoOnErrorConfigured_WhenSecondDiscard_ThenLogsRegularWarning()
    {
        // Arrange
        var capturingLogger = new CapturingLogger();
        var middleware = CreateMiddleware(
            _ => ErrorAction.Discard(),
            customLogger: capturingLogger,
            unconfiguredConsumerName: "OrderConsumer");

        // Act — first discard
        await middleware.InvokeAsync(CreateContext(), _ => throw new InvalidOperationException("first"));
        // Act — second discard
        await middleware.InvokeAsync(CreateContext(), _ => throw new InvalidOperationException("second"));

        // Assert — first is special, second is regular
        Assert.Equal(2, capturingLogger.Entries.Count);
        Assert.Contains("no OnError configured", capturingLogger.Entries[0].Message);
        Assert.DoesNotContain("no OnError configured", capturingLogger.Entries[1].Message);
        Assert.Contains("Discarding failed message", capturingLogger.Entries[1].Message);
    }

    [Fact]
    public async Task GivenOnErrorConfigured_WhenDiscard_ThenLogsRegularWarning()
    {
        // Arrange
        var capturingLogger = new CapturingLogger();
        var middleware = CreateMiddleware(
            _ => ErrorAction.Discard(),
            customLogger: capturingLogger);
        var context = CreateContext();

        // Act
        await middleware.InvokeAsync(context, _ => throw new InvalidOperationException("fail"));

        // Assert — no special unconfigured warning
        Assert.Single(capturingLogger.Entries);
        Assert.DoesNotContain("no OnError configured", capturingLogger.Entries[0].Message);
    }

    // ── Test infrastructure ──

    private sealed class TestInboundContext : InboundContext<string>;

    private sealed class MessageSourceFeature(IReadOnlyDictionary<string, string> properties) : IMessageSourceFeature
    {
        public IReadOnlyDictionary<string, string> Properties { get; } = properties;
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}

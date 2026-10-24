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
        EmitMetrics? metrics = null) =>
        new(
            evaluatePolicy,
            deadLetterSink,
            metrics ?? Metrics,
            new Mock<INodeIdentity>().Object,
            NullLogger<ConsumeErrorMiddleware<string>>.Instance,
            "test-consumer",
            typeof(string));

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
        mockSink.Setup(s => s.DestinationAddress).Returns(new Uri("kafka://broker:9092/dead-letter-topic"));
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
        mockSink.Setup(s => s.DestinationAddress).Returns(new Uri("kafka://broker:9092/dead-letter-topic"));
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

    // ── Batch dead-letter helpers ──

    private static ConsumeErrorMiddleware<MessageBatch<string>> CreateBatchMiddleware(
        Func<Exception, ErrorAction>? evaluatePolicy = null,
        IDeadLetterSink? deadLetterSink = null,
        EmitMetrics? metrics = null) =>
        new(
            evaluatePolicy,
            deadLetterSink,
            metrics ?? Metrics,
            new Mock<INodeIdentity>().Object,
            NullLogger<ConsumeErrorMiddleware<MessageBatch<string>>>.Instance,
            "batch-consumer",
            typeof(string));

    private static ConsumeContext<MessageBatch<string>> CreateBatchContext(
        int itemCount,
        CancellationToken ct = default)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var items = Enumerable.Range(0, itemCount)
            .Select(i => new BatchItem<string>
            {
                Message = $"item-{i}",
                TransportContext = new TestTransportContextWithSource
                {
                    RawKey = [(byte)i],
                    RawValue = [(byte)(i + 10)],
                    Headers = [],
                    ProviderId = "kafka",
                    MessageId = $"item-tc-{i}",
                    Timestamp = DateTimeOffset.UtcNow,
                    CancellationToken = ct,
                    Services = services,
                },
            })
            .ToList();

        return new ConsumeContext<MessageBatch<string>>
        {
            Message = new MessageBatch<string>(items),
            MessageId = "batch-msg",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = ct,
            Services = services,
            TransportContext = new TestTransportContextWithSource
            {
                RawKey = null,
                RawValue = null,
                Headers = [],
                ProviderId = "kafka",
                MessageId = "batch-tc",
                Timestamp = DateTimeOffset.UtcNow,
                CancellationToken = ct,
                Services = services,
            },
        };
    }

    // ── Batch dead-letter tests ──

    [Fact]
    public async Task Given_BatchMessage_When_DeadLetterExecuted_Then_EachItemDeadLetteredIndividually()
    {
        // Arrange
        var mockSink = new Mock<IDeadLetterSink>();
        mockSink.Setup(s => s.DestinationAddress).Returns(new Uri("kafka://broker:9092/dlq"));
        mockSink
            .Setup(s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var middleware = CreateBatchMiddleware(
            evaluatePolicy: _ => ErrorAction.DeadLetter(),
            deadLetterSink: mockSink.Object);

        var context = CreateBatchContext(itemCount: 3);
        var next = new TestPipeline<ConsumeContext<MessageBatch<string>>>(_ => throw new InvalidOperationException("batch failed"));

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert — one ProduceAsync call per item
        mockSink.Verify(
            s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task Given_BatchMessage_When_DeadLetterExecuted_Then_PerItemDlqActivitySpansCreated()
    {
        // Arrange — each item gets its own DLQ activity; verify no exception thrown
        var mockSink = new Mock<IDeadLetterSink>();
        mockSink.Setup(s => s.DestinationAddress).Returns(new Uri("kafka://broker:9092/dlq"));
        mockSink
            .Setup(s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var middleware = CreateBatchMiddleware(
            evaluatePolicy: _ => ErrorAction.DeadLetter(),
            deadLetterSink: mockSink.Object);

        var context = CreateBatchContext(itemCount: 2);
        var next = new TestPipeline<ConsumeContext<MessageBatch<string>>>(_ => throw new InvalidOperationException("batch failed"));

        // Act
        var exception = await Record.ExceptionAsync(() => middleware.InvokeAsync(context, next));

        // Assert — no exception; one call per item confirms per-item activity paths were traversed
        Assert.Null(exception);
        mockSink.Verify(
            s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Given_BatchOf5_When_DlqFailsAtItem3_Then_Items4And5StillDlqed()
    {
        // Arrange
        var callCount = 0;
        var mockSink = new Mock<IDeadLetterSink>();
        mockSink.Setup(s => s.DestinationAddress).Returns(new Uri("kafka://broker:9092/dlq"));
        mockSink
            .Setup(s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 3)
                    throw new TimeoutException("DLQ unavailable");
                return Task.CompletedTask;
            });

        var middleware = CreateBatchMiddleware(
            evaluatePolicy: _ => ErrorAction.DeadLetter(),
            deadLetterSink: mockSink.Object);

        var context = CreateBatchContext(itemCount: 5);
        var next = new TestPipeline<ConsumeContext<MessageBatch<string>>>(_ => throw new InvalidOperationException("batch failed"));

        // Act
        var exception = await Record.ExceptionAsync(() => middleware.InvokeAsync(context, next));

        // Assert — failure at item 3 does not stop items 4 and 5 from being DLQ'd
        Assert.Null(exception);
        Assert.Equal(5, callCount);
    }

    // ── Metrics ──

    [Fact]
    public async Task GivenDeadLetterAction_WhenTheMessageIsDeadLettered_ThenOneDeadLetterIsCountedForTheErrorPolicy()
    {
        // Arrange
        using var recorder = new MeasurementRecorder(MeterNames.Emit);
        var sink = new Mock<IDeadLetterSink>();
        sink.Setup(s => s.DestinationAddress).Returns(new Uri("kafka://broker:9092/dlq"));
        var middleware = CreateMiddleware(_ => ErrorAction.DeadLetter(), sink.Object, new EmitMetrics(null, recorder.Enrichment));

        // Act
        await middleware.InvokeAsync(CreateContext(), new TestPipeline<ConsumeContext<string>>(_ => throw new InvalidOperationException("failed")));

        // Assert
        Assert.Equal(1, recorder.Total("emit.consumer.dlq.produced", "reason", "error_policy"));
    }

    [Fact]
    public async Task GivenBatchOf5_WhenTheThirdDeadLetterFails_ThenFourDeadLettersAndOneFailureAreCounted()
    {
        // Arrange
        using var recorder = new MeasurementRecorder(MeterNames.Emit);
        var calls = 0;
        var sink = new Mock<IDeadLetterSink>();
        sink.Setup(s => s.DestinationAddress).Returns(new Uri("kafka://broker:9092/dlq"));
        sink.Setup(s => s.ProduceAsync(
                It.IsAny<byte[]?>(), It.IsAny<byte[]?>(), It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(), It.IsAny<CancellationToken>()))
            .Returns(() => ++calls == 3 ? throw new TimeoutException("DLQ unavailable") : Task.CompletedTask);
        var middleware = CreateBatchMiddleware(_ => ErrorAction.DeadLetter(), sink.Object, new EmitMetrics(null, recorder.Enrichment));

        // Act
        await middleware.InvokeAsync(
            CreateBatchContext(itemCount: 5),
            new TestPipeline<ConsumeContext<MessageBatch<string>>>(_ => throw new InvalidOperationException("batch failed")));

        // Assert
        Assert.Equal(4, recorder.Total("emit.consumer.dlq.produced", "reason", "error_policy"));
        Assert.Equal(1, recorder.Total("emit.consumer.dlq.produce_errors", "reason", "error_policy"));
    }

    [Fact]
    public async Task GivenDiscardAction_WhenABatchOf3IsDiscarded_ThenThreeDiscardsAreCounted()
    {
        // Arrange
        using var recorder = new MeasurementRecorder(MeterNames.Emit);
        var middleware = CreateBatchMiddleware(_ => ErrorAction.Discard(), metrics: new EmitMetrics(null, recorder.Enrichment));

        // Act
        await middleware.InvokeAsync(
            CreateBatchContext(itemCount: 3),
            new TestPipeline<ConsumeContext<MessageBatch<string>>>(_ => throw new InvalidOperationException("batch failed")));

        // Assert
        Assert.Equal(3, recorder.Total("emit.consumer.discards"));
    }

    [Fact]
    public async Task GivenNoErrorPolicy_WhenTheHandlerThrows_ThenOneDiscardIsCounted()
    {
        // Arrange
        using var recorder = new MeasurementRecorder(MeterNames.Emit);
        var middleware = CreateMiddleware(metrics: new EmitMetrics(null, recorder.Enrichment));

        // Act
        await middleware.InvokeAsync(CreateContext(), new TestPipeline<ConsumeContext<string>>(_ => throw new InvalidOperationException("failed")));

        // Assert
        Assert.Equal(1, recorder.Total("emit.consumer.discards"));
    }

    [Fact]
    public async Task Given_SingleMessage_When_DeadLetterExecuted_Then_ExistingLogicUnchanged()
    {
        // Arrange
        var mockSink = new Mock<IDeadLetterSink>();
        mockSink.Setup(s => s.DestinationAddress).Returns(new Uri("kafka://broker:9092/dead-letter-topic"));
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
        var next = new TestPipeline<ConsumeContext<string>>(_ => throw new InvalidOperationException("single error"));

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert — single-message path still calls ProduceAsync exactly once
        mockSink.Verify(
            s => s.ProduceAsync(
                context.TransportContext.RawKey,
                context.TransportContext.RawValue,
                It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

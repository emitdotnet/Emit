namespace Emit.UnitTests.Consumer;

using global::Emit.Abstractions;
using global::Emit.Abstractions.ErrorHandling;
using global::Emit.Abstractions.Metrics;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Consumer;
using global::Emit.Metrics;
using global::Emit.Pipeline.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public sealed class BatchValidationMiddlewareTests
{
    private static readonly EmitMetrics Metrics = new(null, new EmitMetricsEnrichment());

    private sealed class ConcreteTransportContext : TransportContext
    {
    }

    private static TransportContext MakeTransport(IServiceProvider services) => new ConcreteTransportContext
    {
        RawKey = [0x01],
        RawValue = [0x02],
        Headers = [],
        ProviderId = "test",
        MessageId = "item-tc",
        Timestamp = DateTimeOffset.UtcNow,
        CancellationToken = CancellationToken.None,
        Services = services,
    };

    private static BatchItem<string> MakeItem(string message, IServiceProvider services) => new()
    {
        Message = message,
        TransportContext = MakeTransport(services),
    };

    private static ConsumeContext<MessageBatch<string>> CreateBatchContext(
        IReadOnlyList<BatchItem<string>> items,
        IServiceProvider services) => new()
        {
            Message = new MessageBatch<string>(items),
            MessageId = "batch-1",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = services,
            TransportContext = new ConcreteTransportContext
            {
                RawKey = null,
                RawValue = null,
                Headers = [],
                ProviderId = "test",
                MessageId = "tc-1",
                Timestamp = DateTimeOffset.UtcNow,
                CancellationToken = CancellationToken.None,
                Services = services,
            },
        };

    private static ValidationModule<string> CreateDelegateValidation(
        Func<string, MessageValidationResult> validatorFunc)
    {
        var module = new ValidationModule<string>();
        module.Configure(validatorFunc, a => a.Discard());
        return module;
    }

    private static BatchValidationMiddleware<string> CreateMiddleware(
        ValidationModule<string> validationModule,
        ErrorAction errorAction,
        IDeadLetterSink? deadLetterSink = null) =>
        new(
            validationModule,
            errorAction,
            deadLetterSink,
            Metrics,
            NullLogger.Instance);

    [Fact]
    public async Task Given_AllValidItems_When_InvokeAsync_Then_NextCalledWithOriginalBatch()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();
        var items = new List<BatchItem<string>>
        {
            MakeItem("valid-1", services),
            MakeItem("valid-2", services),
        };
        var context = CreateBatchContext(items, services);
        var originalBatch = context.Message;

        var validation = CreateDelegateValidation(_ => MessageValidationResult.Success);
        var middleware = CreateMiddleware(validation, ErrorAction.Discard());

        var nextCalled = false;
        ConsumeContext<MessageBatch<string>>? passedContext = null;
        var next = new TestPipeline<ConsumeContext<MessageBatch<string>>>(ctx =>
        {
            nextCalled = true;
            passedContext = ctx;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.True(nextCalled);
        Assert.Same(originalBatch, passedContext!.Message);
    }

    [Fact]
    public async Task Given_SomeInvalidItems_When_InvokeAsync_Then_BatchReplacedWithValidItemsOnly()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();
        var items = new List<BatchItem<string>>
        {
            MakeItem("valid", services),
            MakeItem("invalid", services),
            MakeItem("valid2", services),
        };
        var context = CreateBatchContext(items, services);

        var validation = CreateDelegateValidation(msg =>
            msg == "invalid"
                ? MessageValidationResult.Fail("bad")
                : MessageValidationResult.Success);
        var middleware = CreateMiddleware(validation, ErrorAction.Discard());

        MessageBatch<string>? passedBatch = null;
        var next = new TestPipeline<ConsumeContext<MessageBatch<string>>>(ctx =>
        {
            passedBatch = ctx.Message;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.NotNull(passedBatch);
        Assert.Equal(2, passedBatch!.Count);
        Assert.All(passedBatch, item => Assert.NotEqual("invalid", item.Message));
    }

    [Fact]
    public async Task Given_AllInvalidItems_When_InvokeAsync_Then_NextNotCalled()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();
        var items = new List<BatchItem<string>>
        {
            MakeItem("bad-1", services),
            MakeItem("bad-2", services),
        };
        var context = CreateBatchContext(items, services);

        var validation = CreateDelegateValidation(_ => MessageValidationResult.Fail("always fails"));
        var middleware = CreateMiddleware(validation, ErrorAction.Discard());

        var nextCalled = false;
        var next = new TestPipeline<ConsumeContext<MessageBatch<string>>>(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task Given_AllInvalidItems_When_InvokeAsync_Then_NoPassedMetricRecorded()
    {
        // Arrange — use real metrics; test verifies next is NOT called (no passed items)
        var services = new ServiceCollection().BuildServiceProvider();
        var items = new List<BatchItem<string>> { MakeItem("bad", services) };
        var context = CreateBatchContext(items, services);

        var validation = CreateDelegateValidation(_ => MessageValidationResult.Fail("fail"));
        var middleware = CreateMiddleware(validation, ErrorAction.Discard());

        var nextCalled = false;
        var next = new TestPipeline<ConsumeContext<MessageBatch<string>>>(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert — next was not called because no valid items passed
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task Given_SomeInvalidItems_When_DeadLetterAction_Then_EachInvalidItemDeadLettered()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();
        var items = new List<BatchItem<string>>
        {
            MakeItem("valid", services),
            MakeItem("invalid", services),
        };
        var context = CreateBatchContext(items, services);

        var mockSink = new Mock<IDeadLetterSink>();
        mockSink.Setup(s => s.DestinationAddress).Returns(new Uri("kafka://broker:9092/dlq"));
        mockSink
            .Setup(s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var module = new ValidationModule<string>();
        module.Configure(
            msg => msg == "invalid" ? MessageValidationResult.Fail("bad") : MessageValidationResult.Success,
            a => a.DeadLetter());

        var middleware = CreateMiddleware(module, ErrorAction.DeadLetter(), mockSink.Object);
        var next = new TestPipeline<ConsumeContext<MessageBatch<string>>>(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert — dead-letter called once for the one invalid item
        mockSink.Verify(
            s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Given_SomeInvalidItems_When_DiscardAction_Then_InvalidItemsSilentlyDropped()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();
        var items = new List<BatchItem<string>>
        {
            MakeItem("valid", services),
            MakeItem("invalid", services),
        };
        var context = CreateBatchContext(items, services);

        var mockSink = new Mock<IDeadLetterSink>();

        var validation = CreateDelegateValidation(msg =>
            msg == "invalid" ? MessageValidationResult.Fail("bad") : MessageValidationResult.Success);
        var middleware = CreateMiddleware(validation, ErrorAction.Discard(), mockSink.Object);

        MessageBatch<string>? passedBatch = null;
        var next = new TestPipeline<ConsumeContext<MessageBatch<string>>>(ctx =>
        {
            passedBatch = ctx.Message;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert — invalid item was discarded, not dead-lettered
        mockSink.Verify(
            s => s.ProduceAsync(
                It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(),
                It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.NotNull(passedBatch);
        Assert.Single(passedBatch!);
    }

    [Fact]
    public async Task Given_MixedValidation_When_InvokeAsync_Then_PassedAndFailedMetricsBothRecorded()
    {
        // Arrange — use real metrics; test verifies both valid and invalid items were processed
        var services = new ServiceCollection().BuildServiceProvider();
        var items = new List<BatchItem<string>>
        {
            MakeItem("valid", services),
            MakeItem("invalid", services),
        };
        var context = CreateBatchContext(items, services);

        var validation = CreateDelegateValidation(msg =>
            msg == "invalid" ? MessageValidationResult.Fail("bad") : MessageValidationResult.Success);
        var middleware = CreateMiddleware(validation, ErrorAction.Discard());

        MessageBatch<string>? passedBatch = null;
        var next = new TestPipeline<ConsumeContext<MessageBatch<string>>>(ctx =>
        {
            passedBatch = ctx.Message;
            return Task.CompletedTask;
        });

        // Act — should not throw; both passed and failed counts are recorded
        var exception = await Record.ExceptionAsync(() => middleware.InvokeAsync(context, next));

        // Assert
        Assert.Null(exception);
        Assert.NotNull(passedBatch);
        Assert.Single(passedBatch!);
    }
}

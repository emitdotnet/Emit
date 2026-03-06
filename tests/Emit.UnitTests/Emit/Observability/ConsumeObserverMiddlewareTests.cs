namespace Emit.UnitTests.Observability;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Observability;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class ConsumeObserverMiddlewareTests
{
    [Fact]
    public async Task GivenNoObservers_WhenInvokeAsync_ThenNextIsCalled()
    {
        // Arrange
        var middleware = new ConsumeObserverMiddleware<string>(
            [],
            NullLogger<ConsumeObserverMiddleware<string>>.Instance);

        var context = CreateContext();
        var nextCalled = false;
        MessageDelegate<InboundContext<string>> next = ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task GivenObserver_WhenInvokeAsync_ThenOnConsumingCalledBeforeNext()
    {
        // Arrange
        var callSequence = new List<string>();
        var mockObserver = new Mock<IConsumeObserver>();

        mockObserver
            .Setup(o => o.OnConsumingAsync(It.IsAny<InboundContext<string>>()))
            .Callback(() => callSequence.Add("OnConsuming"))
            .Returns(Task.CompletedTask);

        mockObserver
            .Setup(o => o.OnConsumedAsync(It.IsAny<InboundContext<string>>()))
            .Callback(() => callSequence.Add("OnConsumed"))
            .Returns(Task.CompletedTask);

        var middleware = new ConsumeObserverMiddleware<string>(
            [mockObserver.Object],
            NullLogger<ConsumeObserverMiddleware<string>>.Instance);

        var context = CreateContext();
        MessageDelegate<InboundContext<string>> next = ctx =>
        {
            callSequence.Add("Next");
            return Task.CompletedTask;
        };

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.Equal(["OnConsuming", "Next", "OnConsumed"], callSequence);
    }

    [Fact]
    public async Task GivenNextThrows_WhenInvokeAsync_ThenOnConsumeErrorCalledAndExceptionRethrown()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Pipeline failed");
        var mockObserver = new Mock<IConsumeObserver>();

        var middleware = new ConsumeObserverMiddleware<string>(
            [mockObserver.Object],
            NullLogger<ConsumeObserverMiddleware<string>>.Instance);

        var context = CreateContext();
        MessageDelegate<InboundContext<string>> next = _ => throw expectedException;

        // Act & Assert
        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await middleware.InvokeAsync(context, next));

        Assert.Same(expectedException, actualException);
        mockObserver.Verify(
            o => o.OnConsumeErrorAsync(It.IsAny<InboundContext<string>>(), expectedException),
            Times.Once);
        mockObserver.Verify(
            o => o.OnConsumedAsync(It.IsAny<InboundContext<string>>()),
            Times.Never);
    }

    [Fact]
    public async Task GivenObserverThrows_WhenOnConsumingThrows_ThenNextStillCalled()
    {
        // Arrange
        var mockObserver = new Mock<IConsumeObserver>();
        mockObserver
            .Setup(o => o.OnConsumingAsync(It.IsAny<InboundContext<string>>()))
            .ThrowsAsync(new InvalidOperationException("Observer failed"));

        var middleware = new ConsumeObserverMiddleware<string>(
            [mockObserver.Object],
            NullLogger<ConsumeObserverMiddleware<string>>.Instance);

        var context = CreateContext();
        var nextCalled = false;
        MessageDelegate<InboundContext<string>> next = ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.True(nextCalled);
    }

    private static TestInboundContext<string> CreateContext()
    {
        return new TestInboundContext<string>
        {
            MessageId = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = Mock.Of<IServiceProvider>(),
            Message = "test-message"
        };
    }

    private sealed class TestInboundContext<T> : InboundContext<T>;
}

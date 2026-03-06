namespace Emit.UnitTests.Mediator.Observability;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Mediator.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class MediatorObserverMiddlewareTests
{
    [Fact]
    public async Task GivenNoObservers_WhenInvokeAsync_ThenNextIsCalled()
    {
        // Arrange
        var middleware = new MediatorObserverMiddleware<string>(
            [],
            NullLogger<MediatorObserverMiddleware<string>>.Instance);

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
    public async Task GivenObserver_WhenInvokeAsync_ThenOnHandlingCalledBeforeNext()
    {
        // Arrange
        var callSequence = new List<string>();
        var mockObserver = new Mock<IMediatorObserver>();

        mockObserver
            .Setup(o => o.OnHandlingAsync(It.IsAny<InboundContext<string>>()))
            .Callback(() => callSequence.Add("OnHandling"))
            .Returns(Task.CompletedTask);

        mockObserver
            .Setup(o => o.OnHandledAsync(It.IsAny<InboundContext<string>>()))
            .Callback(() => callSequence.Add("OnHandled"))
            .Returns(Task.CompletedTask);

        var middleware = new MediatorObserverMiddleware<string>(
            [mockObserver.Object],
            NullLogger<MediatorObserverMiddleware<string>>.Instance);

        var context = CreateContext();
        MessageDelegate<InboundContext<string>> next = ctx =>
        {
            callSequence.Add("Next");
            return Task.CompletedTask;
        };

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.Equal(["OnHandling", "Next", "OnHandled"], callSequence);
    }

    [Fact]
    public async Task GivenNextThrows_WhenInvokeAsync_ThenOnHandleErrorCalledAndExceptionRethrown()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Pipeline failed");
        var mockObserver = new Mock<IMediatorObserver>();

        var middleware = new MediatorObserverMiddleware<string>(
            [mockObserver.Object],
            NullLogger<MediatorObserverMiddleware<string>>.Instance);

        var context = CreateContext();
        MessageDelegate<InboundContext<string>> next = _ => throw expectedException;

        // Act & Assert
        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await middleware.InvokeAsync(context, next));

        Assert.Same(expectedException, actualException);
        mockObserver.Verify(
            o => o.OnHandleErrorAsync(It.IsAny<InboundContext<string>>(), expectedException),
            Times.Once);
        mockObserver.Verify(
            o => o.OnHandledAsync(It.IsAny<InboundContext<string>>()),
            Times.Never);
    }

    [Fact]
    public async Task GivenObserverThrows_WhenOnHandlingThrows_ThenNextStillCalled()
    {
        // Arrange
        var mockObserver = new Mock<IMediatorObserver>();
        mockObserver
            .Setup(o => o.OnHandlingAsync(It.IsAny<InboundContext<string>>()))
            .ThrowsAsync(new InvalidOperationException("Observer failed"));

        var middleware = new MediatorObserverMiddleware<string>(
            [mockObserver.Object],
            NullLogger<MediatorObserverMiddleware<string>>.Instance);

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

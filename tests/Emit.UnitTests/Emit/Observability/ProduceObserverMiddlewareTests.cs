namespace Emit.UnitTests.Observability;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Observability;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class ProduceObserverMiddlewareTests
{
    [Fact]
    public async Task GivenNoObservers_WhenInvokeAsync_ThenNextIsCalled()
    {
        // Arrange
        var middleware = new ProduceObserverMiddleware<string>(
            [],
            NullLogger<ProduceObserverMiddleware<string>>.Instance);

        var context = CreateContext();
        var nextCalled = false;
        MessageDelegate<OutboundContext<string>> next = ctx =>
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
    public async Task GivenObserver_WhenInvokeAsync_ThenOnProducingCalledBeforeNext()
    {
        // Arrange
        var callSequence = new List<string>();
        var mockObserver = new Mock<IProduceObserver>();

        mockObserver
            .Setup(o => o.OnProducingAsync(It.IsAny<OutboundContext<string>>()))
            .Callback(() => callSequence.Add("OnProducing"))
            .Returns(Task.CompletedTask);

        mockObserver
            .Setup(o => o.OnProducedAsync(It.IsAny<OutboundContext<string>>()))
            .Callback(() => callSequence.Add("OnProduced"))
            .Returns(Task.CompletedTask);

        var middleware = new ProduceObserverMiddleware<string>(
            [mockObserver.Object],
            NullLogger<ProduceObserverMiddleware<string>>.Instance);

        var context = CreateContext();
        MessageDelegate<OutboundContext<string>> next = ctx =>
        {
            callSequence.Add("Next");
            return Task.CompletedTask;
        };

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.Equal(["OnProducing", "Next", "OnProduced"], callSequence);
    }

    [Fact]
    public async Task GivenMultipleObservers_WhenInvokeAsync_ThenAllObserversInvoked()
    {
        // Arrange
        var mockObserver1 = new Mock<IProduceObserver>();
        var mockObserver2 = new Mock<IProduceObserver>();

        var middleware = new ProduceObserverMiddleware<string>(
            [mockObserver1.Object, mockObserver2.Object],
            NullLogger<ProduceObserverMiddleware<string>>.Instance);

        var context = CreateContext();
        MessageDelegate<OutboundContext<string>> next = _ => Task.CompletedTask;

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        mockObserver1.Verify(o => o.OnProducingAsync(It.IsAny<OutboundContext<string>>()), Times.Once);
        mockObserver1.Verify(o => o.OnProducedAsync(It.IsAny<OutboundContext<string>>()), Times.Once);
        mockObserver2.Verify(o => o.OnProducingAsync(It.IsAny<OutboundContext<string>>()), Times.Once);
        mockObserver2.Verify(o => o.OnProducedAsync(It.IsAny<OutboundContext<string>>()), Times.Once);
    }

    [Fact]
    public async Task GivenObserverThrows_WhenOnProducingThrows_ThenNextStillCalled()
    {
        // Arrange
        var mockObserver = new Mock<IProduceObserver>();
        mockObserver
            .Setup(o => o.OnProducingAsync(It.IsAny<OutboundContext<string>>()))
            .ThrowsAsync(new InvalidOperationException("Observer failed"));

        var middleware = new ProduceObserverMiddleware<string>(
            [mockObserver.Object],
            NullLogger<ProduceObserverMiddleware<string>>.Instance);

        var context = CreateContext();
        var nextCalled = false;
        MessageDelegate<OutboundContext<string>> next = ctx =>
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
    public async Task GivenNextThrows_WhenInvokeAsync_ThenOnProduceErrorCalledAndExceptionRethrown()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Pipeline failed");
        var mockObserver = new Mock<IProduceObserver>();

        var middleware = new ProduceObserverMiddleware<string>(
            [mockObserver.Object],
            NullLogger<ProduceObserverMiddleware<string>>.Instance);

        var context = CreateContext();
        MessageDelegate<OutboundContext<string>> next = _ => throw expectedException;

        // Act & Assert
        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await middleware.InvokeAsync(context, next));

        Assert.Same(expectedException, actualException);
        mockObserver.Verify(
            o => o.OnProduceErrorAsync(It.IsAny<OutboundContext<string>>(), expectedException),
            Times.Once);
        mockObserver.Verify(
            o => o.OnProducedAsync(It.IsAny<OutboundContext<string>>()),
            Times.Never);
    }

    [Fact]
    public async Task GivenObserverThrows_WhenOnProducedThrows_ThenNoExceptionPropagated()
    {
        // Arrange
        var mockObserver = new Mock<IProduceObserver>();
        mockObserver
            .Setup(o => o.OnProducedAsync(It.IsAny<OutboundContext<string>>()))
            .ThrowsAsync(new InvalidOperationException("Observer failed"));

        var middleware = new ProduceObserverMiddleware<string>(
            [mockObserver.Object],
            NullLogger<ProduceObserverMiddleware<string>>.Instance);

        var context = CreateContext();
        MessageDelegate<OutboundContext<string>> next = _ => Task.CompletedTask;

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert - no exception thrown
    }

    private static TestOutboundContext<string> CreateContext()
    {
        return new TestOutboundContext<string>
        {
            MessageId = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = Mock.Of<IServiceProvider>(),
            Message = "test-message"
        };
    }

    private sealed class TestOutboundContext<T> : OutboundContext<T>;
}

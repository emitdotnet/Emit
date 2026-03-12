namespace Emit.UnitTests.Observability;

using global::Emit.Abstractions.Metrics;
using global::Emit.Abstractions.Observability;
using global::Emit.Metrics;
using global::Emit.Models;
using global::Emit.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class OutboxObserverInvokerTests
{
    [Fact]
    public async Task GivenNoObservers_WhenOnProcessingAsync_ThenNoException()
    {
        // Arrange
        var invoker = new OutboxObserverInvoker(
            [],
            new OutboxMetrics(null, new EmitMetricsEnrichment()),
            NullLogger<OutboxObserverInvoker>.Instance);

        var entry = CreateTestEntry();

        // Act
        await invoker.OnProcessingAsync(entry, CancellationToken.None);

        // Assert - no exception thrown
    }

    [Fact]
    public async Task GivenObserver_WhenOnEnqueuedAsync_ThenObserverCalled()
    {
        // Arrange
        var mockObserver = new Mock<IOutboxObserver>();
        var invoker = new OutboxObserverInvoker(
            [mockObserver.Object],
            new OutboxMetrics(null, new EmitMetricsEnrichment()),
            NullLogger<OutboxObserverInvoker>.Instance);

        var entry = CreateTestEntry();

        // Act
        await invoker.OnEnqueuedAsync(entry, CancellationToken.None);

        // Assert
        mockObserver.Verify(
            o => o.OnEnqueuedAsync(entry, CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task GivenMultipleObservers_WhenOnProcessedAsync_ThenAllObserversCalled()
    {
        // Arrange
        var mockObserver1 = new Mock<IOutboxObserver>();
        var mockObserver2 = new Mock<IOutboxObserver>();
        var invoker = new OutboxObserverInvoker(
            [mockObserver1.Object, mockObserver2.Object],
            new OutboxMetrics(null, new EmitMetricsEnrichment()),
            NullLogger<OutboxObserverInvoker>.Instance);

        var entry = CreateTestEntry();

        // Act
        await invoker.OnProcessedAsync(entry, CancellationToken.None);

        // Assert
        mockObserver1.Verify(
            o => o.OnProcessedAsync(entry, CancellationToken.None),
            Times.Once);
        mockObserver2.Verify(
            o => o.OnProcessedAsync(entry, CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task GivenObserverThrows_WhenOnProcessErrorAsync_ThenOtherObserversStillCalled()
    {
        // Arrange
        var mockObserver1 = new Mock<IOutboxObserver>();
        var mockObserver2 = new Mock<IOutboxObserver>();

        mockObserver1
            .Setup(o => o.OnProcessErrorAsync(
                It.IsAny<OutboxEntry>(),
                It.IsAny<Exception>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Observer1 failed"));

        var invoker = new OutboxObserverInvoker(
            [mockObserver1.Object, mockObserver2.Object],
            new OutboxMetrics(null, new EmitMetricsEnrichment()),
            NullLogger<OutboxObserverInvoker>.Instance);

        var entry = CreateTestEntry();
        var exception = new Exception("Processing error");

        // Act
        await invoker.OnProcessErrorAsync(entry, exception, CancellationToken.None);

        // Assert
        mockObserver2.Verify(
            o => o.OnProcessErrorAsync(entry, exception, CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public void GivenNoObservers_WhenHasObservers_ThenReturnsFalse()
    {
        // Arrange
        var invoker = new OutboxObserverInvoker(
            [],
            new OutboxMetrics(null, new EmitMetricsEnrichment()),
            NullLogger<OutboxObserverInvoker>.Instance);

        // Act
        var result = invoker.HasObservers;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GivenObservers_WhenHasObservers_ThenReturnsTrue()
    {
        // Arrange
        var mockObserver = new Mock<IOutboxObserver>();
        var invoker = new OutboxObserverInvoker(
            [mockObserver.Object],
            new OutboxMetrics(null, new EmitMetricsEnrichment()),
            NullLogger<OutboxObserverInvoker>.Instance);

        // Act
        var result = invoker.HasObservers;

        // Assert
        Assert.True(result);
    }

    private static OutboxEntry CreateTestEntry()
    {
        return new OutboxEntry
        {
            SystemId = "kafka",
            Destination = "kafka://localhost:9092/test-topic",
            GroupKey = "kafka:test-topic",
            Body = [1, 2, 3]
        };
    }
}

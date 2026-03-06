namespace Emit.UnitTests.Provider.Kafka.Observability;

using global::Emit.Kafka.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class KafkaConsumerObserverInvokerTests
{
    [Fact]
    public async Task GivenNoObservers_WhenOnConsumerStartedAsync_ThenNoException()
    {
        // Arrange
        var invoker = new KafkaConsumerObserverInvoker(
            [],
            NullLogger<KafkaConsumerObserverInvoker>.Instance);

        var e = new ConsumerStartedEvent("test-group", "test-topic", 1);

        // Act
        await invoker.OnConsumerStartedAsync(e);

        // Assert - no exception thrown
    }

    [Fact]
    public async Task GivenObserver_WhenOnPartitionsAssignedAsync_ThenObserverCalled()
    {
        // Arrange
        var mockObserver = new Mock<IKafkaConsumerObserver>();
        var invoker = new KafkaConsumerObserverInvoker(
            [mockObserver.Object],
            NullLogger<KafkaConsumerObserverInvoker>.Instance);

        var e = new PartitionsAssignedEvent("test-group", "test-topic", [0, 1, 2]);

        // Act
        await invoker.OnPartitionsAssignedAsync(e);

        // Assert
        mockObserver.Verify(
            o => o.OnPartitionsAssignedAsync(e),
            Times.Once);
    }

    [Fact]
    public async Task GivenObserverThrows_WhenOnOffsetsCommittedAsync_ThenOtherObserversStillCalled()
    {
        // Arrange
        var mockObserver1 = new Mock<IKafkaConsumerObserver>();
        var mockObserver2 = new Mock<IKafkaConsumerObserver>();

        mockObserver1
            .Setup(o => o.OnOffsetsCommittedAsync(It.IsAny<OffsetsCommittedEvent>()))
            .ThrowsAsync(new InvalidOperationException("Observer1 failed"));

        var invoker = new KafkaConsumerObserverInvoker(
            [mockObserver1.Object, mockObserver2.Object],
            NullLogger<KafkaConsumerObserverInvoker>.Instance);

        var e = new OffsetsCommittedEvent("test-group", []);

        // Act
        await invoker.OnOffsetsCommittedAsync(e);

        // Assert
        mockObserver2.Verify(
            o => o.OnOffsetsCommittedAsync(e),
            Times.Once);
    }

    [Fact]
    public async Task GivenObserver_WhenOnDeserializationErrorAsync_ThenObserverCalled()
    {
        // Arrange
        var mockObserver = new Mock<IKafkaConsumerObserver>();
        var invoker = new KafkaConsumerObserverInvoker(
            [mockObserver.Object],
            NullLogger<KafkaConsumerObserverInvoker>.Instance);

        var exception = new InvalidOperationException("Deserialization failed");
        var e = new DeserializationErrorEvent("test-group", "test-topic", 0, 123, exception);

        // Act
        await invoker.OnDeserializationErrorAsync(e);

        // Assert
        mockObserver.Verify(
            o => o.OnDeserializationErrorAsync(e),
            Times.Once);
    }

    [Fact]
    public void GivenNoObservers_WhenHasObservers_ThenReturnsFalse()
    {
        // Arrange
        var invoker = new KafkaConsumerObserverInvoker(
            [],
            NullLogger<KafkaConsumerObserverInvoker>.Instance);

        // Act
        var result = invoker.HasObservers;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GivenObservers_WhenHasObservers_ThenReturnsTrue()
    {
        // Arrange
        var mockObserver = new Mock<IKafkaConsumerObserver>();
        var invoker = new KafkaConsumerObserverInvoker(
            [mockObserver.Object],
            NullLogger<KafkaConsumerObserverInvoker>.Instance);

        // Act
        var result = invoker.HasObservers;

        // Assert
        Assert.True(result);
    }
}

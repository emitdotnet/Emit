namespace Emit.Kafka.Tests.Consumer;

using global::Emit.Kafka.Consumer;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public sealed class KafkaConsumerFlowControlTests : IDisposable
{
    private readonly Mock<ConfluentKafka.IConsumer<byte[], byte[]>> mockConsumer = new();
    private readonly KafkaConsumerFlowControl sut;

    public KafkaConsumerFlowControlTests()
    {
        sut = new KafkaConsumerFlowControl(NullLogger<KafkaConsumerFlowControl>.Instance);
        sut.SetConsumer(mockConsumer.Object);
    }

    [Fact]
    public async Task GivenAssignedPartitions_WhenPauseAsync_ThenConsumerPauseCalled()
    {
        // Arrange
        var partitions = new List<ConfluentKafka.TopicPartition>
        {
            new("orders", 0),
            new("orders", 1),
        };
        mockConsumer.Setup(c => c.Assignment).Returns(partitions);

        // Act
        await sut.PauseAsync(CancellationToken.None);

        // Assert
        Assert.True(sut.IsPaused);
        mockConsumer.Verify(c => c.Pause(partitions), Times.Once);
    }

    [Fact]
    public async Task GivenPausedConsumer_WhenResumeAsync_ThenConsumerResumeCalled()
    {
        // Arrange
        var partitions = new List<ConfluentKafka.TopicPartition>
        {
            new("orders", 0),
        };
        mockConsumer.Setup(c => c.Assignment).Returns(partitions);
        await sut.PauseAsync(CancellationToken.None);

        // Act
        await sut.ResumeAsync(CancellationToken.None);

        // Assert
        Assert.False(sut.IsPaused);
        mockConsumer.Verify(c => c.Resume(partitions), Times.Once);
    }

    [Fact]
    public async Task GivenAlreadyPaused_WhenPauseAsyncCalledAgain_ThenSkipped()
    {
        // Arrange
        var partitions = new List<ConfluentKafka.TopicPartition> { new("orders", 0) };
        mockConsumer.Setup(c => c.Assignment).Returns(partitions);
        await sut.PauseAsync(CancellationToken.None);

        // Act
        await sut.PauseAsync(CancellationToken.None);

        // Assert
        mockConsumer.Verify(c => c.Pause(It.IsAny<IEnumerable<ConfluentKafka.TopicPartition>>()), Times.Once);
    }

    [Fact]
    public async Task GivenNotPaused_WhenResumeAsyncCalled_ThenSkipped()
    {
        // Arrange
        var partitions = new List<ConfluentKafka.TopicPartition> { new("orders", 0) };
        mockConsumer.Setup(c => c.Assignment).Returns(partitions);

        // Act
        await sut.ResumeAsync(CancellationToken.None);

        // Assert
        Assert.False(sut.IsPaused);
        mockConsumer.Verify(c => c.Resume(It.IsAny<IEnumerable<ConfluentKafka.TopicPartition>>()), Times.Never);
    }

    [Fact]
    public async Task GivenNoAssignedPartitions_WhenPauseAsync_ThenNoPauseCall()
    {
        // Arrange
        mockConsumer.Setup(c => c.Assignment).Returns([]);

        // Act
        await sut.PauseAsync(CancellationToken.None);

        // Assert
        Assert.True(sut.IsPaused);
        mockConsumer.Verify(c => c.Pause(It.IsAny<IEnumerable<ConfluentKafka.TopicPartition>>()), Times.Never);
    }

    [Fact]
    public async Task GivenConcurrentPauseCalls_WhenMultipleThreadsPause_ThenOnlyOneExecutes()
    {
        // Arrange
        var partitions = new List<ConfluentKafka.TopicPartition> { new("orders", 0) };
        mockConsumer.Setup(c => c.Assignment).Returns(partitions);

        // Act
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => sut.PauseAsync(CancellationToken.None))
            .ToArray();
        await Task.WhenAll(tasks);

        // Assert
        Assert.True(sut.IsPaused);
        mockConsumer.Verify(c => c.Pause(It.IsAny<IEnumerable<ConfluentKafka.TopicPartition>>()), Times.Once);
    }

    [Fact]
    public async Task GivenPausedState_WhenPauseIfNeededCalledWithNewPartitions_ThenPartitionsPaused()
    {
        // Arrange
        mockConsumer.Setup(c => c.Assignment).Returns([]);
        await sut.PauseAsync(CancellationToken.None);
        var newPartitions = new List<ConfluentKafka.TopicPartition>
        {
            new("orders", 2),
            new("orders", 3),
        };

        // Act
        sut.PauseIfNeeded(newPartitions);

        // Assert
        mockConsumer.Verify(c => c.Pause(newPartitions), Times.Once);
    }

    [Fact]
    public void GivenNotPaused_WhenPauseIfNeededCalled_ThenNoAction()
    {
        // Arrange
        var partitions = new List<ConfluentKafka.TopicPartition> { new("orders", 0) };

        // Act
        sut.PauseIfNeeded(partitions);

        // Assert
        mockConsumer.Verify(c => c.Pause(It.IsAny<IEnumerable<ConfluentKafka.TopicPartition>>()), Times.Never);
    }

    [Fact]
    public async Task GivenPauseThenResumeThenPause_WhenSequentialCalls_ThenCorrectStateTransitions()
    {
        // Arrange
        var partitions = new List<ConfluentKafka.TopicPartition> { new("orders", 0) };
        mockConsumer.Setup(c => c.Assignment).Returns(partitions);

        // Act & Assert
        await sut.PauseAsync(CancellationToken.None);
        Assert.True(sut.IsPaused);
        mockConsumer.Verify(c => c.Pause(partitions), Times.Once);

        await sut.ResumeAsync(CancellationToken.None);
        Assert.False(sut.IsPaused);
        mockConsumer.Verify(c => c.Resume(partitions), Times.Once);

        await sut.PauseAsync(CancellationToken.None);
        Assert.True(sut.IsPaused);
        mockConsumer.Verify(c => c.Pause(partitions), Times.Exactly(2));
    }

    [Fact]
    public async Task GivenNoConsumerSet_WhenPauseAsync_ThenIsPausedButNoConsumerCall()
    {
        // Arrange
        var flowControl = new KafkaConsumerFlowControl(NullLogger<KafkaConsumerFlowControl>.Instance);
        // Deliberately not calling SetConsumer

        // Act
        await flowControl.PauseAsync(CancellationToken.None);

        // Assert
        Assert.True(flowControl.IsPaused);
        // No consumer calls — no exception
        flowControl.Dispose();
    }

    public void Dispose()
    {
        sut.Dispose();
    }
}

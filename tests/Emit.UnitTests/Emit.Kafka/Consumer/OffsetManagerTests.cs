namespace Emit.Kafka.Tests.Consumer;

using global::Emit.Abstractions.Metrics;
using global::Emit.Kafka.Consumer;
using global::Emit.Kafka.Metrics;
using global::Emit.Kafka.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public sealed class OffsetManagerTests
{
    private static KafkaConsumerObserverInvoker CreateObserverInvoker()
    {
        return new KafkaConsumerObserverInvoker([], NullLogger<KafkaConsumerObserverInvoker>.Instance);
    }

    private static KafkaMetrics CreateKafkaMetrics() => new(null, new EmitMetricsEnrichment());

    private static OffsetCommitter CreateCommitter(ConfluentKafka.IConsumer<byte[], byte[]>? consumer = null)
    {
        consumer ??= Mock.Of<ConfluentKafka.IConsumer<byte[], byte[]>>();
        return new OffsetCommitter(consumer, TimeSpan.FromMinutes(1), "test-group", CreateObserverInvoker(), CreateKafkaMetrics(), NullLogger.Instance);
    }

    [Fact]
    public async Task GivenNewPartition_WhenEnqueue_ThenCreatesPartitionTracker()
    {
        // Arrange
        await using var committer = CreateCommitter();
        var manager = new OffsetManager(committer);
        manager.Enqueue("topic", 0, 1);

        // Act
        manager.MarkAsProcessed("topic", 0, 1);

        // Assert - no exception
    }

    [Fact]
    public async Task GivenMultiplePartitions_WhenEnqueueAndProcess_ThenIndependentTracking()
    {
        // Arrange
        await using var committer = CreateCommitter();
        var manager = new OffsetManager(committer);
        manager.Enqueue("topic", 0, 5);
        manager.Enqueue("topic", 0, 6);
        manager.Enqueue("topic", 1, 10);
        manager.Enqueue("topic", 1, 11);

        // Act
        manager.MarkAsProcessed("topic", 1, 10);
        manager.MarkAsProcessed("topic", 0, 5);

        // Assert - no exception
    }

    [Fact]
    public async Task GivenWatermarkAdvances_WhenMarkAsProcessed_ThenForwardsToCommitter()
    {
        // Arrange
        var mockConsumer = new Mock<ConfluentKafka.IConsumer<byte[], byte[]>>();
        await using var committer = CreateCommitter(mockConsumer.Object);
        var manager = new OffsetManager(committer);
        manager.Enqueue("topic", 0, 5);

        // Act
        manager.MarkAsProcessed("topic", 0, 5);
        committer.Flush();

        // Assert
        mockConsumer.Verify(c => c.Commit(It.Is<IEnumerable<ConfluentKafka.TopicPartitionOffset>>(
            offsets => offsets.Any(o => o.Topic == "topic" && o.Partition.Value == 0 && o.Offset.Value == 6))),
            Times.Once);
    }

    [Fact]
    public async Task GivenWatermarkDoesNotAdvance_WhenMarkAsProcessed_ThenDoesNotForwardToCommitter()
    {
        // Arrange
        var mockConsumer = new Mock<ConfluentKafka.IConsumer<byte[], byte[]>>();
        await using var committer = CreateCommitter(mockConsumer.Object);
        var manager = new OffsetManager(committer);
        manager.Enqueue("topic", 0, 5);
        manager.Enqueue("topic", 0, 6);

        // Act
        manager.MarkAsProcessed("topic", 0, 6);
        committer.Flush();

        // Assert
        mockConsumer.Verify(c => c.Commit(It.IsAny<IEnumerable<ConfluentKafka.TopicPartitionOffset>>()), Times.Never);
    }

    [Fact]
    public async Task GivenUnknownPartition_WhenMarkAsProcessed_ThenSilentlyIgnored()
    {
        // Arrange
        await using var committer = CreateCommitter();
        var manager = new OffsetManager(committer);

        // Act
        manager.MarkAsProcessed("topic", 0, 5);

        // Assert - no exception
    }

    [Fact]
    public async Task GivenClear_WhenCalled_ThenRemovesAllPartitionState()
    {
        // Arrange
        var mockConsumer = new Mock<ConfluentKafka.IConsumer<byte[], byte[]>>();
        await using var committer = CreateCommitter(mockConsumer.Object);
        var manager = new OffsetManager(committer);
        manager.Enqueue("topic", 0, 5);

        // Act
        manager.Clear();
        manager.MarkAsProcessed("topic", 0, 5);
        committer.Flush();

        // Assert
        mockConsumer.Verify(c => c.Commit(It.IsAny<IEnumerable<ConfluentKafka.TopicPartitionOffset>>()), Times.Never);
    }

    [Fact]
    public async Task GivenMultipleTopics_WhenEnqueue_ThenTrackedSeparately()
    {
        // Arrange
        await using var committer = CreateCommitter();
        var manager = new OffsetManager(committer);
        manager.Enqueue("topic-a", 0, 5);
        manager.Enqueue("topic-b", 0, 10);

        // Act
        manager.MarkAsProcessed("topic-a", 0, 5);
        manager.MarkAsProcessed("topic-b", 0, 10);

        // Assert - no exception
    }

    // ── MarkBatchAsProcessed tests ──

    [Fact]
    public async Task Given_TrackedPartition_When_MarkBatchAsProcessed_Then_DelegatesToPartitionOffsets()
    {
        // Arrange — enqueue 3 offsets; process in tail-first order so watermark advances via OOO
        var mockConsumer = new Mock<ConfluentKafka.IConsumer<byte[], byte[]>>();
        await using var committer = CreateCommitter(mockConsumer.Object);
        var manager = new OffsetManager(committer);
        manager.Enqueue("topic", 0, 10);
        manager.Enqueue("topic", 0, 11);
        manager.Enqueue("topic", 0, 12);

        // Act — tail-first: 12→OOO, 11→OOO (head=10), 10→head removed;
        //        then while: OOO.Remove(11)→watermark=11, OOO.Remove(12)→watermark=12
        manager.MarkBatchAsProcessed("topic", 0, [12, 11, 10]);
        committer.Flush();

        // Assert — watermark advances to 12 → commit offset should be 13
        mockConsumer.Verify(c => c.Commit(It.Is<IEnumerable<ConfluentKafka.TopicPartitionOffset>>(
            offsets => offsets.Any(o => o.Topic == "topic" && o.Partition.Value == 0 && o.Offset.Value == 13))),
            Times.Once);
    }

    [Fact]
    public async Task Given_UntrackedPartition_When_MarkBatchAsProcessed_Then_NoOp()
    {
        // Arrange
        var mockConsumer = new Mock<ConfluentKafka.IConsumer<byte[], byte[]>>();
        await using var committer = CreateCommitter(mockConsumer.Object);
        var manager = new OffsetManager(committer);

        // Act — call MarkBatchAsProcessed on a partition that was never enqueued
        manager.MarkBatchAsProcessed("topic", 0, [5, 6, 7]);
        committer.Flush();

        // Assert — no commit since partition was not tracked
        mockConsumer.Verify(c => c.Commit(It.IsAny<IEnumerable<ConfluentKafka.TopicPartitionOffset>>()), Times.Never);
    }
}

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

public sealed class OffsetCommitterTests
{
    private static KafkaConsumerObserverInvoker CreateObserverInvoker()
    {
        return new KafkaConsumerObserverInvoker([], NullLogger<KafkaConsumerObserverInvoker>.Instance);
    }

    private static KafkaMetrics CreateKafkaMetrics() => new(null, new EmitMetricsEnrichment());

    [Fact]
    public async Task GivenRecordCommittableOffset_WhenCalled_ThenStoresOffset()
    {
        // Arrange
        var mockConsumer = new Mock<ConfluentKafka.IConsumer<byte[], byte[]>>();
        await using var committer = new OffsetCommitter(mockConsumer.Object, TimeSpan.FromMinutes(1), "test-group", CreateObserverInvoker(), CreateKafkaMetrics(), NullLogger.Instance);
        committer.RecordCommittableOffset("topic", 0, 8);

        // Act
        committer.Flush();

        // Assert
        mockConsumer.Verify(c => c.Commit(It.IsAny<IEnumerable<ConfluentKafka.TopicPartitionOffset>>()), Times.Once);
    }

    [Fact]
    public async Task GivenRecordCommittableOffset_WhenCalledTwiceSamePartition_ThenLatestWins()
    {
        // Arrange
        var mockConsumer = new Mock<ConfluentKafka.IConsumer<byte[], byte[]>>();
        await using var committer = new OffsetCommitter(mockConsumer.Object, TimeSpan.FromMinutes(1), "test-group", CreateObserverInvoker(), CreateKafkaMetrics(), NullLogger.Instance);
        committer.RecordCommittableOffset("topic", 0, 5);
        committer.RecordCommittableOffset("topic", 0, 8);

        // Act
        committer.Flush();

        // Assert
        mockConsumer.Verify(c => c.Commit(It.Is<IEnumerable<ConfluentKafka.TopicPartitionOffset>>(
            offsets => offsets.Any(o => o.Topic == "topic" && o.Partition.Value == 0 && o.Offset.Value == 9))),
            Times.Once);
    }

    [Fact]
    public async Task GivenFlush_WhenPendingOffsetsExist_ThenCommitsWithPlusOne()
    {
        // Arrange
        var mockConsumer = new Mock<ConfluentKafka.IConsumer<byte[], byte[]>>();
        await using var committer = new OffsetCommitter(mockConsumer.Object, TimeSpan.FromMinutes(1), "test-group", CreateObserverInvoker(), CreateKafkaMetrics(), NullLogger.Instance);
        committer.RecordCommittableOffset("topic", 0, 8);

        // Act
        committer.Flush();

        // Assert
        mockConsumer.Verify(c => c.Commit(It.Is<IEnumerable<ConfluentKafka.TopicPartitionOffset>>(
            offsets => offsets.Any(o => o.Topic == "topic" && o.Partition.Value == 0 && o.Offset.Value == 9))),
            Times.Once);
    }

    [Fact]
    public async Task GivenFlush_WhenNoPendingOffsets_ThenDoesNotCallCommit()
    {
        // Arrange
        var mockConsumer = new Mock<ConfluentKafka.IConsumer<byte[], byte[]>>();
        await using var committer = new OffsetCommitter(mockConsumer.Object, TimeSpan.FromMinutes(1), "test-group", CreateObserverInvoker(), CreateKafkaMetrics(), NullLogger.Instance);

        // Act
        committer.Flush();

        // Assert
        mockConsumer.Verify(c => c.Commit(It.IsAny<IEnumerable<ConfluentKafka.TopicPartitionOffset>>()), Times.Never);
    }

    [Fact]
    public async Task GivenFlush_WhenMultiplePartitions_ThenCommitsAll()
    {
        // Arrange
        var mockConsumer = new Mock<ConfluentKafka.IConsumer<byte[], byte[]>>();
        await using var committer = new OffsetCommitter(mockConsumer.Object, TimeSpan.FromMinutes(1), "test-group", CreateObserverInvoker(), CreateKafkaMetrics(), NullLogger.Instance);
        committer.RecordCommittableOffset("topic", 0, 5);
        committer.RecordCommittableOffset("topic", 1, 10);
        committer.RecordCommittableOffset("topic", 2, 15);

        // Act
        committer.Flush();

        // Assert
        mockConsumer.Verify(c => c.Commit(It.Is<IEnumerable<ConfluentKafka.TopicPartitionOffset>>(
            offsets => offsets.Count() == 3 &&
                       offsets.Any(o => o.Partition.Value == 0 && o.Offset.Value == 6) &&
                       offsets.Any(o => o.Partition.Value == 1 && o.Offset.Value == 11) &&
                       offsets.Any(o => o.Partition.Value == 2 && o.Offset.Value == 16))),
            Times.Once);
    }

    [Fact]
    public async Task GivenFlush_WhenCalled_ThenClearsPendingOffsets()
    {
        // Arrange
        var mockConsumer = new Mock<ConfluentKafka.IConsumer<byte[], byte[]>>();
        await using var committer = new OffsetCommitter(mockConsumer.Object, TimeSpan.FromMinutes(1), "test-group", CreateObserverInvoker(), CreateKafkaMetrics(), NullLogger.Instance);
        committer.RecordCommittableOffset("topic", 0, 5);

        // Act
        committer.Flush();
        committer.Flush();

        // Assert
        mockConsumer.Verify(c => c.Commit(It.IsAny<IEnumerable<ConfluentKafka.TopicPartitionOffset>>()), Times.Once);
    }

    [Fact]
    public async Task GivenOnCommitTimer_WhenTimerFires_ThenCommitsPendingOffsets()
    {
        // Arrange
        var mockConsumer = new Mock<ConfluentKafka.IConsumer<byte[], byte[]>>();
        await using var committer = new OffsetCommitter(mockConsumer.Object, TimeSpan.FromMilliseconds(50), "test-group", CreateObserverInvoker(), CreateKafkaMetrics(), NullLogger.Instance);
        committer.RecordCommittableOffset("topic", 0, 5);

        // Act
        await Task.Delay(500);

        // Assert
        mockConsumer.Verify(c => c.Commit(It.IsAny<IEnumerable<ConfluentKafka.TopicPartitionOffset>>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task GivenDisposeAsync_WhenCalled_ThenStopsTimer()
    {
        // Arrange
        var mockConsumer = new Mock<ConfluentKafka.IConsumer<byte[], byte[]>>();
        var committer = new OffsetCommitter(mockConsumer.Object, TimeSpan.FromMilliseconds(50), "test-group", CreateObserverInvoker(), CreateKafkaMetrics(), NullLogger.Instance);

        // Act
        await committer.DisposeAsync();
        committer.RecordCommittableOffset("topic", 0, 5);
        await Task.Delay(150);

        // Assert
        mockConsumer.Verify(c => c.Commit(It.IsAny<IEnumerable<ConfluentKafka.TopicPartitionOffset>>()), Times.Never);
    }

    [Fact]
    public async Task GivenCommitFails_WhenKafkaException_ThenReQueuesOffsets()
    {
        // Arrange
        var mockConsumer = new Mock<ConfluentKafka.IConsumer<byte[], byte[]>>();
        var callCount = 0;
        mockConsumer.Setup(c => c.Commit(It.IsAny<IEnumerable<ConfluentKafka.TopicPartitionOffset>>()))
            .Callback(() =>
            {
                if (++callCount == 1)
                {
                    throw new ConfluentKafka.KafkaException(new ConfluentKafka.Error(ConfluentKafka.ErrorCode.Local_AllBrokersDown));
                }
            });
        await using var committer = new OffsetCommitter(mockConsumer.Object, TimeSpan.FromMinutes(1), "test-group", CreateObserverInvoker(), CreateKafkaMetrics(), NullLogger.Instance);
        committer.RecordCommittableOffset("topic", 0, 5);

        // Act
        committer.Flush();
        committer.Flush();

        // Assert
        mockConsumer.Verify(c => c.Commit(It.IsAny<IEnumerable<ConfluentKafka.TopicPartitionOffset>>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GivenRecordCommittableOffset_WhenMultipleTopics_ThenTrackedByTopicPartitionKey()
    {
        // Arrange
        var mockConsumer = new Mock<ConfluentKafka.IConsumer<byte[], byte[]>>();
        await using var committer = new OffsetCommitter(mockConsumer.Object, TimeSpan.FromMinutes(1), "test-group", CreateObserverInvoker(), CreateKafkaMetrics(), NullLogger.Instance);
        committer.RecordCommittableOffset("topic-a", 0, 5);
        committer.RecordCommittableOffset("topic-b", 0, 10);

        // Act
        committer.Flush();

        // Assert
        mockConsumer.Verify(c => c.Commit(It.Is<IEnumerable<ConfluentKafka.TopicPartitionOffset>>(
            offsets => offsets.Count() == 2 &&
                       offsets.Any(o => o.Topic == "topic-a" && o.Partition.Value == 0 && o.Offset.Value == 6) &&
                       offsets.Any(o => o.Topic == "topic-b" && o.Partition.Value == 0 && o.Offset.Value == 11))),
            Times.Once);
    }

    [Fact]
    public async Task GivenCommitFails_WhenUnknownMemberIdError_ThenDiscardsOffsetsInsteadOfRequeue()
    {
        // Arrange
        var mockConsumer = new Mock<ConfluentKafka.IConsumer<byte[], byte[]>>();
        mockConsumer.Setup(c => c.Commit(It.IsAny<IEnumerable<ConfluentKafka.TopicPartitionOffset>>()))
            .Throws(new ConfluentKafka.KafkaException(new ConfluentKafka.Error(ConfluentKafka.ErrorCode.UnknownMemberId)));
        await using var committer = new OffsetCommitter(mockConsumer.Object, TimeSpan.FromMinutes(1), "test-group", CreateObserverInvoker(), CreateKafkaMetrics(), NullLogger.Instance);
        committer.RecordCommittableOffset("topic", 0, 5);

        // Act
        committer.Flush();
        committer.Flush();

        // Assert
        mockConsumer.Verify(c => c.Commit(It.IsAny<IEnumerable<ConfluentKafka.TopicPartitionOffset>>()), Times.Once);
    }
}

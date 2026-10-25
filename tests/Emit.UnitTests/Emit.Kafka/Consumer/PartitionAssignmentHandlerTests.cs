namespace Emit.Kafka.Consumer.Tests;

using Moq;
using Xunit;
using static Emit.Kafka.Consumer.Tests.WorkerPoolTestKit;
using ConfluentKafka = Confluent.Kafka;

public sealed class PartitionAssignmentHandlerTests : IDisposable
{
    private static readonly ConfluentKafka.TopicPartition First = new(Topic, 0);
    private static readonly ConfluentKafka.TopicPartition Second = new(Topic, 1);

    // The host the pools run in: stopping it after each test ends the pools the test started
    private readonly CancellationTokenSource host = new();

    [Fact]
    public async Task GivenSomePartitionsRevoked_WhenRevoked_ThenTheKeptOnesAreRewoundToTheirFirstUnhandledMessageAndANewPoolStarts()
    {
        // Arrange: the first partition left offsets 5 and 6 unhandled; the second handled 7 and left 8
        var consumer = CreateConsumer(First, Second);
        var supervisor = CreateSupervisor(new BlockingConsumer(observesCancellation: true));
        await using var committer = CreateCommitter(consumer.Object);
        var handler = new PartitionAssignmentHandler<string, string>(supervisor, committer, host.Token);
        handler.Assigned();
        var stoppedPool = supervisor.Workers;
        handler.OffsetManager.Enqueue(Topic, 0, 5);
        handler.OffsetManager.Enqueue(Topic, 0, 6);
        handler.OffsetManager.Enqueue(Topic, 1, 7);
        handler.OffsetManager.Enqueue(Topic, 1, 8);
        handler.OffsetManager.MarkAsProcessed(Topic, 1, 7);

        // Act
        await handler.RevokedAsync(consumer.Object, new HashSet<ConfluentKafka.TopicPartition> { First });

        // Assert: only the kept partition is rewound, what was handled is committed, and a new pool runs
        consumer.Verify(c => c.Seek(It.IsAny<ConfluentKafka.TopicPartitionOffset>()), Times.Once);
        consumer.Verify(c => c.Seek(It.Is<ConfluentKafka.TopicPartitionOffset>(o => o.TopicPartition == Second && o.Offset == 8)));
        consumer.Verify(c => c.Commit(It.Is<IEnumerable<ConfluentKafka.TopicPartitionOffset>>(
            offsets => offsets.Single().TopicPartition == Second && offsets.Single().Offset == 8)));
        Assert.True(supervisor.IsRunning);
        Assert.NotSame(stoppedPool, supervisor.Workers);
    }

    [Fact]
    public async Task GivenEveryPartitionRevoked_WhenRevoked_ThenNothingIsRewoundAndNoPoolRuns()
    {
        // Arrange
        var consumer = CreateConsumer(First);
        var supervisor = CreateSupervisor(new BlockingConsumer(observesCancellation: true));
        await using var committer = CreateCommitter(consumer.Object);
        var handler = new PartitionAssignmentHandler<string, string>(supervisor, committer, host.Token);
        handler.Assigned();
        handler.OffsetManager.Enqueue(Topic, 0, 5);

        // Act
        await handler.RevokedAsync(consumer.Object, new HashSet<ConfluentKafka.TopicPartition> { First });

        // Assert: the next owner reads the partition from its committed offset
        consumer.Verify(c => c.Seek(It.IsAny<ConfluentKafka.TopicPartitionOffset>()), Times.Never);
        Assert.False(supervisor.IsRunning);
    }

    [Fact]
    public async Task GivenNoRunningPool_WhenPartitionsAreAssigned_ThenAPoolStartsWithANewOffsetManager()
    {
        // Arrange: the partitions were all revoked
        var consumer = CreateConsumer(First);
        var supervisor = CreateSupervisor(new BlockingConsumer(observesCancellation: true));
        await using var committer = CreateCommitter(consumer.Object);
        var handler = new PartitionAssignmentHandler<string, string>(supervisor, committer, host.Token);
        handler.Assigned();
        var stoppedPoolOffsets = handler.OffsetManager;
        await handler.RevokedAsync(consumer.Object, new HashSet<ConfluentKafka.TopicPartition> { First });

        // Act
        handler.Assigned();

        // Assert
        Assert.True(supervisor.IsRunning);
        Assert.NotSame(stoppedPoolOffsets, handler.OffsetManager);
    }

    [Fact]
    public async Task GivenRunningPool_WhenPartitionsAreAssigned_ThenTheSamePoolGoesOn()
    {
        // Arrange: an incremental assignment adds partitions without revoking any
        var supervisor = CreateSupervisor(new BlockingConsumer(observesCancellation: true));
        await using var committer = CreateCommitter();
        var handler = new PartitionAssignmentHandler<string, string>(supervisor, committer, host.Token);
        handler.Assigned();
        var runningPool = supervisor.Workers;

        // Act
        handler.Assigned();

        // Assert
        Assert.Same(runningPool, supervisor.Workers);
    }

    [Fact]
    public async Task GivenPartitionsLost_WhenLost_ThenTheRunningHandlerIsCancelledAtOnceAndNothingIsRewoundOrCommitted()
    {
        // Arrange: a handler running, a stop timeout far longer than the test, an offset handled before
        var consumer = CreateConsumer(First);
        var blocking = new BlockingConsumer(observesCancellation: true);
        var supervisor = CreateSupervisor(blocking, workerStopTimeout: TimeSpan.FromMinutes(5));
        await using var committer = CreateCommitter(consumer.Object);
        var handler = new PartitionAssignmentHandler<string, string>(supervisor, committer, host.Token);
        handler.Assigned();
        handler.OffsetManager.Enqueue(Topic, 0, 4);
        handler.OffsetManager.MarkAsProcessed(Topic, 0, 4);
        handler.OffsetManager.Enqueue(Topic, 0, 5);
        supervisor.Workers![0].Writer.TryWrite(CreateConsumeResult(offset: 5));
        await blocking.Started;

        // Act: what is still pending is never committed, the next commit included
        await handler.LostAsync(consumer.Object, [First]).WaitAsync(TimeSpan.FromSeconds(30));
        committer.Flush();

        // Assert
        Assert.True(blocking.Cancelled);
        consumer.Verify(c => c.Seek(It.IsAny<ConfluentKafka.TopicPartitionOffset>()), Times.Never);
        consumer.Verify(c => c.Commit(It.IsAny<IEnumerable<ConfluentKafka.TopicPartitionOffset>>()), Times.Never);
        Assert.False(supervisor.IsRunning);
    }

    [Fact]
    public async Task GivenFaultedPool_WhenRestarted_ThenEveryAssignedPartitionIsRewoundAndANewPoolStarts()
    {
        // Arrange
        var consumer = CreateConsumer(First, Second);
        var supervisor = CreateSupervisor(new BlockingConsumer(observesCancellation: true));
        await using var committer = CreateCommitter(consumer.Object);
        var handler = new PartitionAssignmentHandler<string, string>(supervisor, committer, host.Token);
        handler.Assigned();
        var faultedPool = supervisor.Workers;
        handler.OffsetManager.Enqueue(Topic, 0, 5);
        handler.OffsetManager.Enqueue(Topic, 1, 7);

        // Act
        await handler.RestartAfterFaultAsync(consumer.Object);

        // Assert
        consumer.Verify(c => c.Seek(It.Is<ConfluentKafka.TopicPartitionOffset>(o => o.TopicPartition == First && o.Offset == 5)));
        consumer.Verify(c => c.Seek(It.Is<ConfluentKafka.TopicPartitionOffset>(o => o.TopicPartition == Second && o.Offset == 7)));
        Assert.True(supervisor.IsRunning);
        Assert.NotSame(faultedPool, supervisor.Workers);
    }

    [Fact]
    public async Task GivenRunningPool_WhenAMessageIsConsumed_ThenAWorkerOfThePoolTakesIt()
    {
        // Arrange
        var supervisor = CreateSupervisor(new BlockingConsumer(observesCancellation: true));
        await using var committer = CreateCommitter();
        var handler = new PartitionAssignmentHandler<string, string>(supervisor, committer, host.Token);
        handler.Assigned();

        // Act
        var worker = handler.WorkerFor(CreateConsumeResult(offset: 0));

        // Assert
        Assert.Same(supervisor.Workers![0], worker);
    }

    [Fact]
    public async Task GivenHostStopping_WhenPartitionsAreAssigned_ThenNoWorkerTakesTheMessagesConsumed()
    {
        // Arrange: the host stops before the assignment the consume runs
        var supervisor = CreateSupervisor(new BlockingConsumer(observesCancellation: true));
        await using var committer = CreateCommitter();
        var handler = new PartitionAssignmentHandler<string, string>(supervisor, committer, host.Token);
        await host.CancelAsync();
        handler.Assigned();

        // Act
        var worker = handler.WorkerFor(CreateConsumeResult(offset: 0));

        // Assert
        Assert.Null(worker);
    }

    [Fact]
    public async Task GivenHostStopping_WhenSomePartitionsAreRevoked_ThenNoPoolStarts()
    {
        // Arrange
        var consumer = CreateConsumer(First, Second);
        var supervisor = CreateSupervisor(new BlockingConsumer(observesCancellation: true));
        await using var committer = CreateCommitter(consumer.Object);
        var handler = new PartitionAssignmentHandler<string, string>(supervisor, committer, host.Token);
        handler.Assigned();
        await host.CancelAsync();

        // Act
        await handler.RevokedAsync(consumer.Object, new HashSet<ConfluentKafka.TopicPartition> { First });

        // Assert
        Assert.False(supervisor.IsRunning);
    }

    private static Mock<ConfluentKafka.IConsumer<byte[], byte[]>> CreateConsumer(params ConfluentKafka.TopicPartition[] assignment)
    {
        var consumer = new Mock<ConfluentKafka.IConsumer<byte[], byte[]>>();
        consumer.Setup(c => c.Assignment).Returns([.. assignment]);
        return consumer;
    }

    public void Dispose()
    {
        host.Cancel();
        host.Dispose();
    }
}

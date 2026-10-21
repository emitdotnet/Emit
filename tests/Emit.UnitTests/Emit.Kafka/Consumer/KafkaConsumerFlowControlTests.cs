namespace Emit.Kafka.Tests.Consumer;

using global::Emit.Kafka.Consumer;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public sealed class KafkaConsumerFlowControlTests
{
    private static readonly ConfluentKafka.TopicPartition Partition0 = new("orders", 0);
    private static readonly ConfluentKafka.TopicPartition Partition1 = new("orders", 1);

    private readonly Mock<ConfluentKafka.IConsumer<byte[], byte[]>> mockConsumer = new();
    private readonly KafkaConsumerFlowControl sut;

    public KafkaConsumerFlowControlTests()
    {
        sut = new KafkaConsumerFlowControl(NullLogger<KafkaConsumerFlowControl>.Instance);
        sut.SetConsumer(mockConsumer.Object);
    }

    [Fact]
    public async Task GivenAssignedPartitions_WhenPausedThenResumed_ThenEachIsPausedThenResumedOnTheConsumer()
    {
        // Arrange
        sut.OnPartitionsAssigned([Partition0, Partition1]);

        // Act
        await sut.Application.PauseAsync(CancellationToken.None);
        var pausedWhilePaused = sut.IsPaused;
        await sut.Application.ResumeAsync(CancellationToken.None);

        // Assert
        Assert.True(pausedWhilePaused);
        Assert.False(sut.IsPaused);
        mockConsumer.Verify(c => c.Pause(It.Is<IEnumerable<ConfluentKafka.TopicPartition>>(p => p.ToHashSet().SetEquals(new[] { Partition0, Partition1 }))), Times.Once);
        mockConsumer.Verify(c => c.Resume(It.Is<IEnumerable<ConfluentKafka.TopicPartition>>(p => p.ToHashSet().SetEquals(new[] { Partition0, Partition1 }))), Times.Once);
    }

    [Fact]
    public async Task GivenPausedGroup_WhenPausedAgainConcurrently_ThenTheConsumerIsPausedOnce()
    {
        // Arrange
        sut.OnPartitionsAssigned([Partition0]);
        await sut.Application.PauseAsync(CancellationToken.None);

        // Act
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => sut.Application.PauseAsync(CancellationToken.None)));

        // Assert
        mockConsumer.Verify(c => c.Pause(It.IsAny<IEnumerable<ConfluentKafka.TopicPartition>>()), Times.Once);
    }

    [Fact]
    public async Task GivenGroupNotPaused_WhenResumed_ThenTheConsumerIsNotTouched()
    {
        // Arrange
        sut.OnPartitionsAssigned([Partition0]);

        // Act
        await sut.Application.ResumeAsync(CancellationToken.None);
        await sut.CircuitBreaker.ResumeAsync(CancellationToken.None);

        // Assert
        Assert.Empty(mockConsumer.Invocations);
    }

    [Fact]
    public async Task GivenNoAssignedPartitionOrNoConsumer_WhenPaused_ThenTheGroupIsPausedWithoutTouchingAConsumer()
    {
        // Arrange
        var withoutConsumer = new KafkaConsumerFlowControl(NullLogger<KafkaConsumerFlowControl>.Instance);

        // Act
        await sut.Application.PauseAsync(CancellationToken.None);
        await withoutConsumer.Application.PauseAsync(CancellationToken.None);

        // Assert
        Assert.True(sut.IsPaused);
        Assert.True(withoutConsumer.IsPaused);
        Assert.Empty(mockConsumer.Invocations);
    }

    [Fact]
    public async Task GivenPartitionsAssignedButNotYetInTheConsumersAssignment_WhenTheApplicationPauses_ThenTheyArePaused()
    {
        // Arrange: the consumer applies an assignment only after the assigned handler returns, so a pause that
        // lands in between must not rely on the consumer's assignment
        mockConsumer.Setup(c => c.Assignment).Returns([]);
        sut.OnPartitionsAssigned([Partition0]);

        // Act
        await sut.Application.PauseAsync(CancellationToken.None);

        // Assert
        mockConsumer.Verify(c => c.Pause(It.Is<IEnumerable<ConfluentKafka.TopicPartition>>(p => p.Contains(Partition0))), Times.Once);
    }

    [Fact]
    public async Task GivenPartitionNotAssignedByTheClientYet_WhenTheGroupIsPaused_ThenItIsPausedWhenItsFirstMessageArrives()
    {
        // Arrange: the client refuses to pause a partition it has not assigned yet
        mockConsumer
            .Setup(c => c.Pause(It.IsAny<IEnumerable<ConfluentKafka.TopicPartition>>()))
            .Throws(new ConfluentKafka.TopicPartitionException([new(Partition0, new ConfluentKafka.Error(ConfluentKafka.ErrorCode.Local_UnknownPartition))]));
        sut.OnPartitionsAssigned([Partition0]);
        await sut.Application.PauseAsync(CancellationToken.None);
        mockConsumer.Reset();

        // Act
        var handedOver = sut.TryBeginHandling(Partition0);

        // Assert
        Assert.False(handedOver);
        mockConsumer.Verify(c => c.Pause(It.Is<IEnumerable<ConfluentKafka.TopicPartition>>(p => p.Single() == Partition0)), Times.Once);
    }

    [Fact]
    public async Task GivenPartitionRevoked_WhenTheGroupIsPaused_ThenOnlyTheRemainingPartitionsArePaused()
    {
        // Arrange
        sut.OnPartitionsAssigned([Partition0, Partition1]);
        sut.OnPartitionsRevoked([Partition1]);

        // Act
        await sut.Application.PauseAsync(CancellationToken.None);

        // Assert
        mockConsumer.Verify(c => c.Pause(It.Is<IEnumerable<ConfluentKafka.TopicPartition>>(p => p.Single() == Partition0)), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenGroupPausedByTheApplicationAndTheCircuitBreaker_WhenOnlyOneOfThemResumes_ThenItStaysPausedUntilBothHave(bool applicationResumesFirst)
    {
        // Arrange
        sut.OnPartitionsAssigned([Partition0]);
        await sut.Application.PauseAsync(CancellationToken.None);
        await sut.CircuitBreaker.PauseAsync(CancellationToken.None);
        var first = applicationResumesFirst ? sut.Application : sut.CircuitBreaker;
        var second = applicationResumesFirst ? sut.CircuitBreaker : sut.Application;

        // Act
        await first.ResumeAsync(CancellationToken.None);
        var resumedAfterFirst = mockConsumer.Invocations.Any(i => i.Method.Name == nameof(ConfluentKafka.IConsumer<byte[], byte[]>.Resume));
        var handedOverAfterFirst = sut.TryBeginHandling(Partition0);
        await second.ResumeAsync(CancellationToken.None);

        // Assert
        Assert.False(resumedAfterFirst);
        Assert.False(handedOverAfterFirst);
        mockConsumer.Verify(c => c.Resume(It.Is<IEnumerable<ConfluentKafka.TopicPartition>>(p => p.Contains(Partition0))), Times.Once);
        Assert.True(sut.TryBeginHandling(Partition0));
    }

    [Fact]
    public async Task GivenGroupPaused_WhenAMessageFetchedBeforeThePauseIsHandedOver_ThenItIsRefused()
    {
        // Arrange: a message the consumer returned just before the pause must not reach a worker
        sut.OnPartitionsAssigned([Partition0]);
        await sut.Application.PauseAsync(CancellationToken.None);

        // Act
        var handedOver = sut.TryBeginHandling(Partition0);

        // Assert
        Assert.False(handedOver);
    }

    [Fact]
    public async Task GivenMessagesBeingHandled_WhenTheApplicationPauses_ThenThePauseCompletesOnlyOnceTheyEnd()
    {
        // Arrange
        sut.OnPartitionsAssigned([Partition0]);
        Assert.True(sut.TryBeginHandling(Partition0));
        Assert.True(sut.TryBeginHandling(Partition0));

        // Act
        var pause = sut.Application.PauseAsync(CancellationToken.None);
        var completedWhileHandling = pause.IsCompleted;
        sut.EndHandling(1);
        var completedWithOneLeft = pause.IsCompleted;
        sut.EndHandling(1);

        // Assert
        Assert.False(completedWhileHandling);
        Assert.False(completedWithOneLeft);
        await pause.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task GivenApplicationPauseWaitingForMessages_WhenTheApplicationResumes_ThenThePauseReturns()
    {
        // Arrange
        sut.OnPartitionsAssigned([Partition0]);
        Assert.True(sut.TryBeginHandling(Partition0));
        var pause = sut.Application.PauseAsync(CancellationToken.None);

        // Act
        await sut.Application.ResumeAsync(CancellationToken.None);

        // Assert
        await pause.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task GivenMessagesBeingHandled_WhenTheCircuitBreakerPauses_ThenItDoesNotWaitForThem()
    {
        // Arrange: the circuit breaker pauses from inside the message that tripped it, so waiting would deadlock
        sut.OnPartitionsAssigned([Partition0]);
        Assert.True(sut.TryBeginHandling(Partition0));

        // Act
        var pause = sut.CircuitBreaker.PauseAsync(CancellationToken.None);

        // Assert
        Assert.True(pause.IsCompletedSuccessfully);
        await pause;
    }

    [Fact]
    public async Task GivenAHandlerOfTheGroup_WhenItPausesItsOwnGroup_ThenThePauseReturnsWithoutWaitingForItself()
    {
        // Arrange
        sut.OnPartitionsAssigned([Partition0]);
        Assert.True(sut.TryBeginHandling(Partition0));

        // Act
        var pause = Task.Run(async () =>
        {
            sut.EnterHandler();
            await sut.Application.PauseAsync(CancellationToken.None);
        });

        // Assert
        await pause.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(sut.TryBeginHandling(Partition0));
    }

    [Fact]
    public async Task GivenApplicationPauseWaitingForMessages_WhenItsTokenIsCancelled_ThenTheWaitStopsButTheGroupStaysPaused()
    {
        // Arrange
        sut.OnPartitionsAssigned([Partition0]);
        Assert.True(sut.TryBeginHandling(Partition0));
        using var cts = new CancellationTokenSource();
        var pause = sut.Application.PauseAsync(cts.Token);

        // Act
        await cts.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pause.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.False(sut.TryBeginHandling(Partition0));
    }

    [Fact]
    public async Task GivenDetachedConsumer_WhenTheGroupIsPausedAndResumed_ThenTheClosedConsumerIsNotTouched()
    {
        // Arrange: pause and resume can come from any thread while the consumer is being closed
        sut.OnPartitionsAssigned([Partition0]);
        sut.Detach();

        // Act
        await sut.Application.PauseAsync(CancellationToken.None);
        await sut.Application.ResumeAsync(CancellationToken.None);

        // Assert
        Assert.Empty(mockConsumer.Invocations);
    }

    [Fact]
    public async Task GivenMessagesBeingHandledWhenTheConsumerIsDetached_WhenTheApplicationPauses_ThenItDoesNotWaitForThem()
    {
        // Arrange: messages left in the workers at shutdown are never handled
        sut.OnPartitionsAssigned([Partition0]);
        Assert.True(sut.TryBeginHandling(Partition0));
        sut.Detach();

        // Act
        var pause = sut.Application.PauseAsync(CancellationToken.None);

        // Assert
        await pause.WaitAsync(TimeSpan.FromSeconds(30));
    }
}

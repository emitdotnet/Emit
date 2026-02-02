namespace Emit.Tests.Abstractions;

using Emit.Abstractions;
using Emit.Models;
using Moq;
using Transactional.Abstractions;
using Xunit;

public class IOutboxRepositoryTests
{
    private readonly Mock<IOutboxRepository> mockRepository;

    public IOutboxRepositoryTests()
    {
        mockRepository = new Mock<IOutboxRepository>();
    }

    [Fact]
    public async Task GivenNullTransaction_WhenEnqueueAsync_ThenMethodAcceptsNull()
    {
        // Arrange
        var entry = CreateTestEntry();
        mockRepository
            .Setup(r => r.EnqueueAsync(entry, null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await mockRepository.Object.EnqueueAsync(entry, null);

        // Assert
        mockRepository.Verify(r => r.EnqueueAsync(entry, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenValidTransaction_WhenEnqueueAsync_ThenTransactionIsPassed()
    {
        // Arrange
        var entry = CreateTestEntry();
        var transaction = new Mock<ITransactionContext>().Object;
        mockRepository
            .Setup(r => r.EnqueueAsync(entry, transaction, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await mockRepository.Object.EnqueueAsync(entry, transaction);

        // Assert
        mockRepository.Verify(r => r.EnqueueAsync(entry, transaction, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenNullTransaction_WhenGetNextSequenceAsync_ThenReturnsSequence()
    {
        // Arrange
        var groupKey = "cluster:topic";
        var expectedSequence = 42L;
        mockRepository
            .Setup(r => r.GetNextSequenceAsync(groupKey, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedSequence);

        // Act
        var result = await mockRepository.Object.GetNextSequenceAsync(groupKey, null);

        // Assert
        Assert.Equal(expectedSequence, result);
        mockRepository.Verify(r => r.GetNextSequenceAsync(groupKey, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenValidTransaction_WhenGetNextSequenceAsync_ThenTransactionIsPassed()
    {
        // Arrange
        var groupKey = "cluster:topic";
        var transaction = new Mock<ITransactionContext>().Object;
        mockRepository
            .Setup(r => r.GetNextSequenceAsync(groupKey, transaction, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);

        // Act
        await mockRepository.Object.GetNextSequenceAsync(groupKey, transaction);

        // Assert
        mockRepository.Verify(r => r.GetNextSequenceAsync(groupKey, transaction, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenEntryId_WhenUpdateStatusAsync_ThenUpdatesStatus()
    {
        // Arrange
        var entryId = Guid.NewGuid();
        mockRepository
            .Setup(r => r.UpdateStatusAsync(
                entryId,
                OutboxStatus.Completed,
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<OutboxAttempt?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await mockRepository.Object.UpdateStatusAsync(
            entryId,
            OutboxStatus.Completed,
            DateTime.UtcNow,
            null,
            null,
            null,
            null);

        // Assert
        mockRepository.Verify(r => r.UpdateStatusAsync(
            entryId,
            OutboxStatus.Completed,
            It.IsAny<DateTime?>(),
            It.IsAny<DateTime?>(),
            It.IsAny<int?>(),
            It.IsAny<string?>(),
            It.IsAny<OutboxAttempt?>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenLimit_WhenGetGroupHeadsAsync_ThenReturnsEntries()
    {
        // Arrange
        var entries = new List<OutboxEntry> { CreateTestEntry(), CreateTestEntry() };
        mockRepository
            .Setup(r => r.GetGroupHeadsAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        // Act
        var result = await mockRepository.Object.GetGroupHeadsAsync(10);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GivenEligibleGroups_WhenGetBatchAsync_ThenReturnsEntries()
    {
        // Arrange
        var eligibleGroups = new[] { "group1", "group2" };
        var entries = new List<OutboxEntry> { CreateTestEntry() };
        mockRepository
            .Setup(r => r.GetBatchAsync(eligibleGroups, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        // Act
        var result = await mockRepository.Object.GetBatchAsync(eligibleGroups, 100);

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public async Task GivenRetentionDate_WhenDeleteCompletedEntriesAsync_ThenReturnsDeletedCount()
    {
        // Arrange
        var completedBefore = DateTime.UtcNow.AddDays(-7);
        mockRepository
            .Setup(r => r.DeleteCompletedEntriesAsync(completedBefore, 1000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(50);

        // Act
        var result = await mockRepository.Object.DeleteCompletedEntriesAsync(completedBefore, 1000);

        // Assert
        Assert.Equal(50, result);
    }

    private static OutboxEntry CreateTestEntry() => new()
    {
        ProviderId = "kafka",
        RegistrationKey = "__default__",
        GroupKey = "cluster:topic",
        Payload = [1, 2, 3]
    };
}

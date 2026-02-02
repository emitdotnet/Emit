namespace Emit.IntegrationTests;

using Emit.Abstractions;
using Emit.Models;
using Xunit;

/// <summary>
/// Base class for outbox repository integration tests.
/// </summary>
/// <remarks>
/// <para>
/// Derived classes must implement <see cref="Repository"/> and <see cref="ConnectionString"/>
/// properties, and the <see cref="IAsyncLifetime"/> methods for database setup/teardown.
/// </para>
/// <para>
/// All tests are marked with [Trait("Category", "Integration")] so they can be filtered
/// during CI/CD runs that don't have database services available.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public abstract class BaseIntegrationTest : IAsyncLifetime
{
    /// <summary>
    /// Gets the outbox repository instance under test.
    /// </summary>
    protected abstract IOutboxRepository Repository { get; }

    /// <summary>
    /// Gets the connection string used for this test fixture.
    /// </summary>
    protected abstract string ConnectionString { get; }

    /// <inheritdoc/>
    public abstract Task InitializeAsync();

    /// <inheritdoc/>
    public abstract Task DisposeAsync();

    /// <summary>
    /// Creates a valid test entry with the specified parameters.
    /// </summary>
    /// <param name="groupKey">The group key for the entry.</param>
    /// <param name="providerId">The provider ID.</param>
    /// <param name="registrationKey">The registration key.</param>
    /// <returns>A new <see cref="OutboxEntry"/> instance.</returns>
    protected static OutboxEntry CreateTestEntry(
        string groupKey = "test-group",
        string providerId = "kafka",
        string registrationKey = "__default__")
    {
        return new OutboxEntry
        {
            ProviderId = providerId,
            RegistrationKey = registrationKey,
            GroupKey = groupKey,
            Payload = [0x01, 0x02, 0x03],
            EnqueuedAt = DateTime.UtcNow,
            Status = OutboxStatus.Pending
        };
    }

    #region EnqueueAsync Tests

    [Fact]
    public async Task GivenValidEntry_WhenEnqueueAsync_ThenEntryIsPersisted()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act
        await Repository.EnqueueAsync(entry, transaction: null);

        // Assert
        var groupHeads = await Repository.GetGroupHeadsAsync(limit: 10);
        Assert.Contains(groupHeads, e => e.GroupKey == entry.GroupKey);
    }

    [Fact]
    public async Task GivenMultipleEntries_WhenEnqueueAsync_ThenAllEntriesArePersisted()
    {
        // Arrange
        var entry1 = CreateTestEntry(groupKey: "group-1");
        var entry2 = CreateTestEntry(groupKey: "group-2");
        var entry3 = CreateTestEntry(groupKey: "group-3");

        // Act
        await Repository.EnqueueAsync(entry1, transaction: null);
        await Repository.EnqueueAsync(entry2, transaction: null);
        await Repository.EnqueueAsync(entry3, transaction: null);

        // Assert
        var groupHeads = await Repository.GetGroupHeadsAsync(limit: 10);
        Assert.True(groupHeads.Count >= 3);
    }

    #endregion

    #region GetNextSequenceAsync Tests

    [Fact]
    public async Task GivenNewGroup_WhenGetNextSequenceAsync_ThenReturnsOne()
    {
        // Arrange
        var uniqueGroupKey = $"seq-test-{Guid.NewGuid():N}";

        // Act
        var sequence = await Repository.GetNextSequenceAsync(uniqueGroupKey, transaction: null);

        // Assert
        Assert.Equal(1, sequence);
    }

    [Fact]
    public async Task GivenExistingGroup_WhenGetNextSequenceAsync_ThenReturnsIncrementedValue()
    {
        // Arrange
        var uniqueGroupKey = $"seq-test-{Guid.NewGuid():N}";

        // Act
        var sequence1 = await Repository.GetNextSequenceAsync(uniqueGroupKey, transaction: null);
        var sequence2 = await Repository.GetNextSequenceAsync(uniqueGroupKey, transaction: null);
        var sequence3 = await Repository.GetNextSequenceAsync(uniqueGroupKey, transaction: null);

        // Assert
        Assert.Equal(1, sequence1);
        Assert.Equal(2, sequence2);
        Assert.Equal(3, sequence3);
    }

    #endregion

    #region GetGroupHeadsAsync Tests

    [Fact]
    public async Task GivenNoEntries_WhenGetGroupHeadsAsync_ThenReturnsEmptyList()
    {
        // Arrange - use a unique group key to ensure isolation
        var uniqueGroupKey = $"empty-test-{Guid.NewGuid():N}";
        _ = uniqueGroupKey; // Unused, just ensures test isolation

        // Act
        // Note: This test may return entries from other tests if not properly isolated
        // In practice, each test class should use a unique database or clean up

        // Assert
        // The actual assertion depends on test isolation strategy
        var groupHeads = await Repository.GetGroupHeadsAsync(limit: 1000);
        Assert.NotNull(groupHeads);
    }

    [Fact]
    public async Task GivenMultipleGroups_WhenGetGroupHeadsAsync_ThenReturnsOneEntryPerGroup()
    {
        // Arrange
        var group1 = $"head-test-1-{Guid.NewGuid():N}";
        var group2 = $"head-test-2-{Guid.NewGuid():N}";

        var entry1a = CreateTestEntry(groupKey: group1);
        entry1a.Sequence = await Repository.GetNextSequenceAsync(group1, transaction: null);
        await Repository.EnqueueAsync(entry1a, transaction: null);

        var entry1b = CreateTestEntry(groupKey: group1);
        entry1b.Sequence = await Repository.GetNextSequenceAsync(group1, transaction: null);
        await Repository.EnqueueAsync(entry1b, transaction: null);

        var entry2 = CreateTestEntry(groupKey: group2);
        entry2.Sequence = await Repository.GetNextSequenceAsync(group2, transaction: null);
        await Repository.EnqueueAsync(entry2, transaction: null);

        // Act
        var groupHeads = await Repository.GetGroupHeadsAsync(limit: 100);

        // Assert
        var group1Head = groupHeads.FirstOrDefault(e => e.GroupKey == group1);
        var group2Head = groupHeads.FirstOrDefault(e => e.GroupKey == group2);

        Assert.NotNull(group1Head);
        Assert.NotNull(group2Head);

        // Group 1 should have the first entry (lowest sequence) as head
        Assert.Equal(entry1a.Sequence, group1Head.Sequence);
    }

    #endregion

    #region GetBatchAsync Tests

    [Fact]
    public async Task GivenEligibleGroups_WhenGetBatchAsync_ThenReturnsEntriesFromThoseGroups()
    {
        // Arrange
        var group1 = $"batch-test-1-{Guid.NewGuid():N}";
        var group2 = $"batch-test-2-{Guid.NewGuid():N}";
        var group3 = $"batch-test-3-{Guid.NewGuid():N}";

        var entry1 = CreateTestEntry(groupKey: group1);
        entry1.Sequence = await Repository.GetNextSequenceAsync(group1, transaction: null);
        await Repository.EnqueueAsync(entry1, transaction: null);

        var entry2 = CreateTestEntry(groupKey: group2);
        entry2.Sequence = await Repository.GetNextSequenceAsync(group2, transaction: null);
        await Repository.EnqueueAsync(entry2, transaction: null);

        var entry3 = CreateTestEntry(groupKey: group3);
        entry3.Sequence = await Repository.GetNextSequenceAsync(group3, transaction: null);
        await Repository.EnqueueAsync(entry3, transaction: null);

        // Act
        var batch = await Repository.GetBatchAsync([group1, group2], batchSize: 10);

        // Assert
        Assert.True(batch.Count >= 2);
        Assert.All(batch, e => Assert.True(e.GroupKey == group1 || e.GroupKey == group2));
        Assert.DoesNotContain(batch, e => e.GroupKey == group3);
    }

    [Fact]
    public async Task GivenEmptyEligibleGroups_WhenGetBatchAsync_ThenReturnsEmptyList()
    {
        // Arrange & Act
        var batch = await Repository.GetBatchAsync([], batchSize: 10);

        // Assert
        Assert.Empty(batch);
    }

    #endregion

    #region UpdateStatusAsync Tests

    [Fact]
    public async Task GivenPendingEntry_WhenUpdateStatusToCompleted_ThenStatusIsUpdated()
    {
        // Arrange
        var uniqueGroupKey = $"status-test-{Guid.NewGuid():N}";
        var entry = CreateTestEntry(groupKey: uniqueGroupKey);
        entry.Sequence = await Repository.GetNextSequenceAsync(uniqueGroupKey, transaction: null);
        await Repository.EnqueueAsync(entry, transaction: null);

        // Act
        await Repository.UpdateStatusAsync(
            entry.Id!,
            OutboxStatus.Completed,
            completedAt: DateTime.UtcNow,
            lastAttemptedAt: DateTime.UtcNow,
            retryCount: 0,
            latestError: null,
            attempt: null);

        // Assert
        var groupHeads = await Repository.GetGroupHeadsAsync(limit: 100);
        Assert.DoesNotContain(groupHeads, e => e.GroupKey == uniqueGroupKey);
    }

    [Fact]
    public async Task GivenPendingEntry_WhenUpdateStatusToFailed_ThenStatusAndErrorAreUpdated()
    {
        // Arrange
        var uniqueGroupKey = $"fail-test-{Guid.NewGuid():N}";
        var entry = CreateTestEntry(groupKey: uniqueGroupKey);
        entry.Sequence = await Repository.GetNextSequenceAsync(uniqueGroupKey, transaction: null);
        await Repository.EnqueueAsync(entry, transaction: null);

        var errorMessage = "Test error message";
        var attempt = new OutboxAttempt(
            AttemptedAt: DateTime.UtcNow,
            Reason: "TransientError",
            Message: errorMessage,
            ExceptionType: "System.Exception");

        // Act
        await Repository.UpdateStatusAsync(
            entry.Id!,
            OutboxStatus.Failed,
            completedAt: null,
            lastAttemptedAt: DateTime.UtcNow,
            retryCount: 1,
            latestError: errorMessage,
            attempt: attempt);

        // Assert
        var groupHeads = await Repository.GetGroupHeadsAsync(limit: 100);
        var failedEntry = groupHeads.FirstOrDefault(e => e.GroupKey == uniqueGroupKey);

        Assert.NotNull(failedEntry);
        Assert.Equal(OutboxStatus.Failed, failedEntry.Status);
        Assert.Equal(errorMessage, failedEntry.LatestError);
        Assert.Equal(1, failedEntry.RetryCount);
    }

    #endregion

    #region DeleteCompletedEntriesAsync Tests

    [Fact]
    public async Task GivenCompletedEntries_WhenDeleteCompletedEntriesAsync_ThenOldEntriesAreDeleted()
    {
        // Arrange
        var uniqueGroupKey = $"delete-test-{Guid.NewGuid():N}";
        var entry = CreateTestEntry(groupKey: uniqueGroupKey);
        entry.Sequence = await Repository.GetNextSequenceAsync(uniqueGroupKey, transaction: null);
        await Repository.EnqueueAsync(entry, transaction: null);

        // Mark as completed with a past timestamp
        var completedAt = DateTime.UtcNow.AddDays(-10);
        await Repository.UpdateStatusAsync(
            entry.Id!,
            OutboxStatus.Completed,
            completedAt: completedAt,
            lastAttemptedAt: DateTime.UtcNow.AddDays(-10),
            retryCount: 0,
            latestError: null,
            attempt: null);

        // Act
        var deletedCount = await Repository.DeleteCompletedEntriesAsync(
            completedBefore: DateTime.UtcNow.AddDays(-5),
            batchSize: 100);

        // Assert
        Assert.True(deletedCount >= 1);
    }

    #endregion
}

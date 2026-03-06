namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Xunit;

/// <summary>
/// Compliance tests for distributed lock behavior. Derived classes provide a database-specific
/// <see cref="IDistributedLockProvider"/> instance.
/// </summary>
[Trait("Category", "Integration")]
public abstract class DistributedLockCompliance : IAsyncLifetime
{
    /// <summary>
    /// Gets the distributed lock provider under test.
    /// </summary>
    protected abstract IDistributedLockProvider LockProvider { get; }

    /// <inheritdoc/>
    public abstract Task InitializeAsync();

    /// <inheritdoc/>
    public abstract Task DisposeAsync();

    [Fact]
    public async Task GivenNewKey_WhenTryAcquire_ThenSucceeds()
    {
        // Act
        await using var @lock = await LockProvider.TryAcquireAsync("new-key", TimeSpan.FromSeconds(30));

        // Assert
        Assert.NotNull(@lock);
        Assert.Equal("new-key", @lock.Key);
    }

    [Fact]
    public async Task GivenHeldKey_WhenTryAcquireWithZeroTimeout_ThenReturnsNull()
    {
        // Arrange
        await using var first = await LockProvider.TryAcquireAsync("held-key", TimeSpan.FromSeconds(30));
        Assert.NotNull(first);

        // Act
        var second = await LockProvider.TryAcquireAsync("held-key", TimeSpan.FromSeconds(30));

        // Assert
        Assert.Null(second);
    }

    [Fact]
    public async Task GivenExpiredLock_WhenTryAcquire_ThenSucceeds()
    {
        // Arrange — acquire with short TTL, don't dispose
        var first = await LockProvider.TryAcquireAsync("expired-key", TimeSpan.FromSeconds(1));
        Assert.NotNull(first);

        // Wait for TTL to expire
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Act
        await using var second = await LockProvider.TryAcquireAsync("expired-key", TimeSpan.FromSeconds(30));

        // Assert
        Assert.NotNull(second);

        // Dispose the original handle (no-op since lock was already taken over)
        await first.DisposeAsync();
    }

    [Fact]
    public async Task GivenDisposedHandle_WhenTryAcquireSameKey_ThenSucceeds()
    {
        // Arrange
        var first = await LockProvider.TryAcquireAsync("release-key", TimeSpan.FromSeconds(30));
        Assert.NotNull(first);
        await first.DisposeAsync();

        // Act
        await using var second = await LockProvider.TryAcquireAsync("release-key", TimeSpan.FromSeconds(30));

        // Assert
        Assert.NotNull(second);
    }

    [Fact]
    public async Task GivenConcurrentAcquisitions_WhenSameKey_ThenExactlyOneSucceeds()
    {
        // Act
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => LockProvider.TryAcquireAsync("concurrent-key", TimeSpan.FromSeconds(30)));
        var results = await Task.WhenAll(tasks);

        // Assert
        var acquired = results.Where(r => r is not null).ToList();
        Assert.Single(acquired);

        // Cleanup
        foreach (var handle in acquired)
        {
            await handle!.DisposeAsync();
        }
    }

    [Fact]
    public async Task GivenTimeout_WhenHolderReleasesWithinWindow_ThenAcquiresSuccessfully()
    {
        // Arrange
        var first = await LockProvider.TryAcquireAsync("timeout-release-key", TimeSpan.FromSeconds(30));
        Assert.NotNull(first);

        // Start waiting acquisition with timeout
        var acquireTask = LockProvider.TryAcquireAsync(
            "timeout-release-key", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

        // Release the first lock after a short delay
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await first.DisposeAsync();

        // Act
        await using var second = await acquireTask;

        // Assert
        Assert.NotNull(second);
    }

    [Fact]
    public async Task GivenTimeout_WhenLockNeverReleased_ThenReturnsNull()
    {
        // Arrange
        await using var first = await LockProvider.TryAcquireAsync("timeout-fail-key", TimeSpan.FromSeconds(30));
        Assert.NotNull(first);

        // Act
        var second = await LockProvider.TryAcquireAsync(
            "timeout-fail-key", TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500));

        // Assert
        Assert.Null(second);
    }

    [Fact]
    public async Task GivenCancellationToken_WhenCancelled_ThenThrows()
    {
        // Arrange
        await using var first = await LockProvider.TryAcquireAsync("cancel-key", TimeSpan.FromSeconds(30));
        Assert.NotNull(first);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => LockProvider.TryAcquireAsync(
                "cancel-key", TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan, cts.Token));
    }

    [Fact]
    public async Task GivenDifferentKeys_WhenAcquiredConcurrently_ThenAllSucceed()
    {
        // Act
        var tasks = Enumerable.Range(0, 10)
            .Select(i => LockProvider.TryAcquireAsync($"multi-key-{i}", TimeSpan.FromSeconds(30)));
        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.All(results, r => Assert.NotNull(r));

        // Cleanup
        foreach (var handle in results)
        {
            await handle!.DisposeAsync();
        }
    }

    [Fact]
    public async Task GivenValidLock_WhenExtend_ThenReturnsTrue()
    {
        // Arrange
        await using var @lock = await LockProvider.TryAcquireAsync("extend-key", TimeSpan.FromSeconds(10));
        Assert.NotNull(@lock);

        // Act
        var extended = await @lock.ExtendAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.True(extended);
    }

    [Fact]
    public async Task GivenExpiredLockReacquired_WhenOriginalExtends_ThenReturnsFalse()
    {
        // Arrange — acquire with short TTL
        var first = await LockProvider.TryAcquireAsync("extend-fail-key", TimeSpan.FromSeconds(1));
        Assert.NotNull(first);

        // Wait for TTL to expire and reacquire
        await Task.Delay(TimeSpan.FromSeconds(2));
        await using var second = await LockProvider.TryAcquireAsync("extend-fail-key", TimeSpan.FromSeconds(30));
        Assert.NotNull(second);

        // Act — original handle tries to extend
        var extended = await first.ExtendAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.False(extended);

        await first.DisposeAsync();
    }
}

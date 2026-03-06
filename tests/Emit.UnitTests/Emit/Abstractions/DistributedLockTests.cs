namespace Emit.UnitTests.Abstractions;

using global::Emit.Abstractions;
using Xunit;

public class DistributedLockTests
{
    [Fact]
    public async Task GivenLock_WhenDispose_ThenReleasesWithCorrectKeyAndLockId()
    {
        // Arrange
        var provider = new TestDistributedLockProvider();
        var lockId = Guid.NewGuid();
        var @lock = new DistributedLock(provider, "test-key", lockId);

        // Act
        await @lock.DisposeAsync();

        // Assert
        var call = Assert.Single(provider.ReleaseCalls);
        Assert.Equal("test-key", call.Key);
        Assert.Equal(lockId, call.LockId);
    }

    [Fact]
    public async Task GivenDisposedLock_WhenDisposeAgain_ThenDoesNotReleaseAgain()
    {
        // Arrange
        var provider = new TestDistributedLockProvider();
        var @lock = new DistributedLock(provider, "test-key", Guid.NewGuid());

        // Act
        await @lock.DisposeAsync();
        await @lock.DisposeAsync();
        await @lock.DisposeAsync();

        // Assert
        Assert.Single(provider.ReleaseCalls);
    }

    [Fact]
    public async Task GivenLock_WhenExtend_ThenDelegatesWithCorrectParameters()
    {
        // Arrange
        var provider = new TestDistributedLockProvider();
        var lockId = Guid.NewGuid();
        var @lock = new DistributedLock(provider, "test-key", lockId);
        var ttl = TimeSpan.FromSeconds(30);

        // Act
        var result = await @lock.ExtendAsync(ttl);

        // Assert
        Assert.True(result);
        var call = Assert.Single(provider.ExtendCalls);
        Assert.Equal("test-key", call.Key);
        Assert.Equal(lockId, call.LockId);
        Assert.Equal(ttl, call.Ttl);
    }

    [Fact]
    public async Task GivenDisposedLock_WhenExtend_ThenThrowsObjectDisposedException()
    {
        // Arrange
        var provider = new TestDistributedLockProvider();
        var @lock = new DistributedLock(provider, "test-key", Guid.NewGuid());
        await @lock.DisposeAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => @lock.ExtendAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void GivenLock_WhenAccessKey_ThenReturnsCorrectKey()
    {
        // Arrange
        var provider = new TestDistributedLockProvider();

        // Act
        var @lock = new DistributedLock(provider, "my-resource", Guid.NewGuid());

        // Assert
        Assert.Equal("my-resource", @lock.Key);
    }

    private sealed class TestDistributedLockProvider()
        : DistributedLockProviderBase(new TestRandomProvider())
    {
        public List<(string Key, Guid LockId)> ReleaseCalls { get; } = [];
        public List<(string Key, Guid LockId, TimeSpan Ttl)> ExtendCalls { get; } = [];
        public bool ExtendResult { get; set; } = true;

        protected override Task<bool> TryAcquireCoreAsync(
            string key, Guid lockId, TimeSpan ttl, CancellationToken cancellationToken)
            => Task.FromResult(true);

        protected override Task ReleaseCoreAsync(
            string key, Guid lockId, CancellationToken cancellationToken)
        {
            ReleaseCalls.Add((key, lockId));
            return Task.CompletedTask;
        }

        protected override Task<bool> ExtendCoreAsync(
            string key, Guid lockId, TimeSpan ttl, CancellationToken cancellationToken)
        {
            ExtendCalls.Add((key, lockId, ttl));
            return Task.FromResult(ExtendResult);
        }
    }
}

namespace Emit.UnitTests.Abstractions;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Metrics;
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

    [Theory]
    [InlineData(true, "extended")]
    [InlineData(false, "lost")]
    public async Task GivenLock_WhenExtended_ThenTheRenewalIsCountedWithItsResult(bool extended, string result)
    {
        // Arrange
        using var recorder = new MeasurementRecorder(MeterNames.EmitLock);
        var provider = new TestDistributedLockProvider { ExtendResult = extended };
        var @lock = new DistributedLock(provider, "test-key", Guid.NewGuid(), new LockMetrics(null, recorder.Enrichment));

        // Act
        await @lock.ExtendAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.Equal(1, recorder.Total("emit.lock.renewal.completed", "result", result));
        Assert.Equal(1, recorder.Total("emit.lock.renewal.completed"));
    }

    [Fact]
    public async Task GivenProviderThatThrows_WhenExtended_ThenAFailedRenewalIsCounted()
    {
        // Arrange
        using var recorder = new MeasurementRecorder(MeterNames.EmitLock);
        var provider = new TestDistributedLockProvider { ExtendException = new TimeoutException("database unreachable") };
        var @lock = new DistributedLock(provider, "test-key", Guid.NewGuid(), new LockMetrics(null, recorder.Enrichment));

        // Act
        await Assert.ThrowsAsync<TimeoutException>(() => @lock.ExtendAsync(TimeSpan.FromSeconds(30)));

        // Assert
        Assert.Equal(1, recorder.Total("emit.lock.renewal.completed", "result", "failed"));
    }

    [Fact]
    public async Task GivenCancelledToken_WhenExtended_ThenACancelledRenewalIsCounted()
    {
        // Arrange
        using var recorder = new MeasurementRecorder(MeterNames.EmitLock);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var provider = new TestDistributedLockProvider { ExtendException = new OperationCanceledException(cts.Token) };
        var @lock = new DistributedLock(provider, "test-key", Guid.NewGuid(), new LockMetrics(null, recorder.Enrichment));

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => @lock.ExtendAsync(TimeSpan.FromSeconds(30), cts.Token));

        // Assert
        Assert.Equal(1, recorder.Total("emit.lock.renewal.completed", "result", "cancelled"));
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
        public Exception? ExtendException { get; set; }

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
            return ExtendException is null ? Task.FromResult(ExtendResult) : Task.FromException<bool>(ExtendException);
        }
    }
}

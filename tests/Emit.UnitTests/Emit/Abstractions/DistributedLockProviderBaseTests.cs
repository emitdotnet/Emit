namespace Emit.UnitTests.Abstractions;

using global::Emit.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

public class DistributedLockProviderBaseTests
{
    [Fact]
    public async Task GivenNullKey_WhenTryAcquire_ThenThrowsArgumentNullException()
    {
        // Arrange
        var provider = new TestDistributedLockProvider();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => provider.TryAcquireAsync(null!, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task GivenEmptyKey_WhenTryAcquire_ThenThrowsArgumentException()
    {
        // Arrange
        var provider = new TestDistributedLockProvider();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.TryAcquireAsync("", TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1000)]
    public async Task GivenZeroOrNegativeTtl_WhenTryAcquire_ThenThrowsArgumentOutOfRangeException(int ttlMs)
    {
        // Arrange
        var provider = new TestDistributedLockProvider();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => provider.TryAcquireAsync("key", TimeSpan.FromMilliseconds(ttlMs)));
    }

    [Fact]
    public async Task GivenNegativeTimeout_WhenTryAcquire_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var provider = new TestDistributedLockProvider();

        // Act & Assert — note: TimeSpan.FromMilliseconds(-1) equals Timeout.InfiniteTimeSpan, so use -2
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => provider.TryAcquireAsync("key", TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(-2)));
    }

    [Fact]
    public async Task GivenInfiniteTimeout_WhenTryAcquire_ThenDoesNotThrow()
    {
        // Arrange
        var provider = new TestDistributedLockProvider { AcquireResult = true };

        // Act
        await using var @lock = await provider.TryAcquireAsync(
            "key", TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);

        // Assert
        Assert.NotNull(@lock);
    }

    [Fact]
    public async Task GivenZeroTimeout_WhenAcquireSucceeds_ThenReturnsHandle()
    {
        // Arrange
        var provider = new TestDistributedLockProvider { AcquireResult = true };

        // Act
        await using var @lock = await provider.TryAcquireAsync("key", TimeSpan.FromSeconds(10));

        // Assert
        Assert.NotNull(@lock);
        Assert.Equal("key", @lock.Key);
        Assert.Equal(1, provider.AcquireCallCount);
    }

    [Fact]
    public async Task GivenZeroTimeout_WhenAcquireFails_ThenReturnsNull()
    {
        // Arrange
        var provider = new TestDistributedLockProvider { AcquireResult = false };

        // Act
        var @lock = await provider.TryAcquireAsync("key", TimeSpan.FromSeconds(10));

        // Assert
        Assert.Null(@lock);
        Assert.Equal(1, provider.AcquireCallCount);
    }

    [Fact]
    public async Task GivenTimeout_WhenAcquireAlwaysFails_ThenRetriesAndReturnsNull()
    {
        // Arrange
        var provider = new TestDistributedLockProvider { AcquireResult = false };

        // Act
        var @lock = await provider.TryAcquireAsync(
            "key", TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(500));

        // Assert
        Assert.Null(@lock);
        Assert.True(provider.AcquireCallCount > 1, $"Expected multiple attempts, got {provider.AcquireCallCount}");
    }

    [Fact]
    public async Task GivenCancellationToken_WhenCancelled_ThenThrowsOperationCancelledException()
    {
        // Arrange
        var provider = new TestDistributedLockProvider { AcquireResult = false };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert — TaskCanceledException is a subclass of OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.TryAcquireAsync(
                "key", TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan, cts.Token));
    }

    [Fact]
    public async Task GivenInfiniteTimeout_WhenAcquireSucceedsAfterRetries_ThenReturnsHandle()
    {
        // Arrange
        var attempt = 0;
        var provider = new TestDistributedLockProvider
        {
            AcquireFunc = (_, _, _, _) => ++attempt >= 3
        };

        // Act
        await using var @lock = await provider.TryAcquireAsync(
            "key", TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);

        // Assert
        Assert.NotNull(@lock);
        Assert.Equal(3, provider.AcquireCallCount);
    }

    [Fact]
    public async Task GivenRetries_WhenBackoffApplied_ThenDelaysIncrease()
    {
        // Arrange — fixedDouble=0.5 → jitter factor = 1.0 (no jitter)
        // Expected delays: 100ms, 200ms, 400ms, 800ms
        var fakeTime = new NotifyingFakeTimeProvider();
        var attempt = 0;
        var provider = new TestDistributedLockProvider(fixedDouble: 0.5, timeProvider: fakeTime)
        {
            AcquireFunc = (_, _, _, _) => ++attempt >= 5
        };

        // Act — infinite timeout, succeed on the 5th attempt
        var task = provider.TryAcquireAsync("key", TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);

        // Advance through each expected backoff delay, waiting for each
        // Task.Delay timer to be created before advancing past it
        TimeSpan[] expectedDelays =
        [
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(400),
            TimeSpan.FromMilliseconds(800),
        ];

        foreach (var delay in expectedDelays)
        {
            await fakeTime.TimerCreated.WaitAsync();
            fakeTime.Advance(delay);
        }

        await using var result = await task;

        // Assert — 5 attempts total (4 failures + 1 success)
        Assert.NotNull(result);
        Assert.Equal(5, provider.AcquireCallCount);

        // Verify intervals between calls match expected delays exactly
        var intervals = provider.AcquireTimestamps
            .Zip(provider.AcquireTimestamps.Skip(1), (a, b) => b - a)
            .ToList();

        Assert.Equal(expectedDelays.Length, intervals.Count);
        for (var i = 0; i < expectedDelays.Length; i++)
        {
            Assert.Equal(expectedDelays[i], intervals[i]);
        }
    }

    [Fact]
    public async Task GivenTimeout_WhenAcquireEventuallySucceeds_ThenReturnsHandle()
    {
        // Arrange — succeed on the 3rd attempt
        var attempt = 0;
        var provider = new TestDistributedLockProvider
        {
            AcquireFunc = (_, _, _, _) => ++attempt >= 3
        };

        // Act
        await using var @lock = await provider.TryAcquireAsync(
            "key", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));

        // Assert
        Assert.NotNull(@lock);
        Assert.Equal(3, provider.AcquireCallCount);
    }

    private sealed class TestDistributedLockProvider : DistributedLockProviderBase
    {
        private readonly TimeProvider timeProvider;

        public TestDistributedLockProvider(double fixedDouble = 0.5, TimeProvider? timeProvider = null)
            : base(new TestRandomProvider(fixedDouble: fixedDouble), timeProvider)
        {
            this.timeProvider = timeProvider ?? TimeProvider.System;
        }

        public bool AcquireResult { get; set; } = true;
        public int AcquireCallCount { get; private set; }
        public List<DateTimeOffset> AcquireTimestamps { get; } = [];
        public Func<string, Guid, TimeSpan, CancellationToken, bool>? AcquireFunc { get; set; }

        protected override Task<bool> TryAcquireCoreAsync(
            string key, Guid lockId, TimeSpan ttl, CancellationToken cancellationToken)
        {
            AcquireCallCount++;
            AcquireTimestamps.Add(timeProvider.GetUtcNow());

            if (AcquireFunc is not null)
            {
                return Task.FromResult(AcquireFunc(key, lockId, ttl, cancellationToken));
            }

            return Task.FromResult(AcquireResult);
        }

        protected override Task ReleaseCoreAsync(
            string key, Guid lockId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        protected override Task<bool> ExtendCoreAsync(
            string key, Guid lockId, TimeSpan ttl, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class NotifyingFakeTimeProvider : FakeTimeProvider
    {
        public SemaphoreSlim TimerCreated { get; } = new(0);

        public override ITimer CreateTimer(
            TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            TimerCreated.Release();
            return timer;
        }
    }
}

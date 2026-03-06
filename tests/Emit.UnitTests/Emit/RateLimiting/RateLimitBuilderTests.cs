namespace Emit.Tests.RateLimiting;

using System.Threading.RateLimiting;
using global::Emit.RateLimiting;
using Xunit;

public sealed class RateLimitBuilderTests
{
    // ── TokenBucket ──

    [Fact]
    public void GivenTokenBucket_WhenValidParameters_ThenBuildReturnsRateLimiter()
    {
        // Arrange
        var builder = new RateLimitBuilder();
        builder.TokenBucket(permitsPerSecond: 100, burstSize: 50);

        // Act
        using var limiter = builder.Build(queueLimit: 10);

        // Assert
        Assert.NotNull(limiter);
    }

    [Fact]
    public void GivenTokenBucket_WhenPermitsPerSecondIsZero_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = new RateLimitBuilder();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.TokenBucket(permitsPerSecond: 0, burstSize: 50));
    }

    [Fact]
    public void GivenTokenBucket_WhenPermitsPerSecondIsNegative_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = new RateLimitBuilder();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.TokenBucket(permitsPerSecond: -1, burstSize: 50));
    }

    [Fact]
    public void GivenTokenBucket_WhenBurstSizeIsZero_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = new RateLimitBuilder();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.TokenBucket(permitsPerSecond: 100, burstSize: 0));
    }

    [Fact]
    public void GivenTokenBucket_WhenBurstSizeIsNegative_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = new RateLimitBuilder();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.TokenBucket(permitsPerSecond: 100, burstSize: -5));
    }

    [Fact]
    public void GivenTokenBucket_WhenChaining_ThenReturnsSameBuilder()
    {
        // Arrange
        var builder = new RateLimitBuilder();

        // Act
        var result = builder.TokenBucket(permitsPerSecond: 100, burstSize: 50);

        // Assert
        Assert.Same(builder, result);
    }

    // ── FixedWindow ──

    [Fact]
    public void GivenFixedWindow_WhenValidParameters_ThenBuildReturnsRateLimiter()
    {
        // Arrange
        var builder = new RateLimitBuilder();
        builder.FixedWindow(permits: 100, window: TimeSpan.FromMinutes(1));

        // Act
        using var limiter = builder.Build(queueLimit: 10);

        // Assert
        Assert.NotNull(limiter);
    }

    [Fact]
    public void GivenFixedWindow_WhenPermitsIsZero_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = new RateLimitBuilder();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.FixedWindow(permits: 0, window: TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void GivenFixedWindow_WhenWindowIsZero_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = new RateLimitBuilder();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.FixedWindow(permits: 100, window: TimeSpan.Zero));
    }

    [Fact]
    public void GivenFixedWindow_WhenWindowIsNegative_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = new RateLimitBuilder();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.FixedWindow(permits: 100, window: TimeSpan.FromSeconds(-1)));
    }

    // ── SlidingWindow ──

    [Fact]
    public void GivenSlidingWindow_WhenValidParameters_ThenBuildReturnsRateLimiter()
    {
        // Arrange
        var builder = new RateLimitBuilder();
        builder.SlidingWindow(permits: 100, window: TimeSpan.FromMinutes(1), segmentsPerWindow: 4);

        // Act
        using var limiter = builder.Build(queueLimit: 10);

        // Assert
        Assert.NotNull(limiter);
    }

    [Fact]
    public void GivenSlidingWindow_WhenPermitsIsZero_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = new RateLimitBuilder();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.SlidingWindow(permits: 0, window: TimeSpan.FromMinutes(1), segmentsPerWindow: 4));
    }

    [Fact]
    public void GivenSlidingWindow_WhenWindowIsZero_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = new RateLimitBuilder();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.SlidingWindow(permits: 100, window: TimeSpan.Zero, segmentsPerWindow: 4));
    }

    [Fact]
    public void GivenSlidingWindow_WhenSegmentsIsZero_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = new RateLimitBuilder();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.SlidingWindow(permits: 100, window: TimeSpan.FromMinutes(1), segmentsPerWindow: 0));
    }

    // ── Duplicate configuration ──

    [Fact]
    public void GivenTokenBucket_WhenCalledTwice_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new RateLimitBuilder();
        builder.TokenBucket(permitsPerSecond: 100, burstSize: 50);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.TokenBucket(permitsPerSecond: 200, burstSize: 100));
        Assert.Contains("already been configured", ex.Message);
    }

    [Fact]
    public void GivenTokenBucketThenFixedWindow_WhenCalled_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new RateLimitBuilder();
        builder.TokenBucket(permitsPerSecond: 100, burstSize: 50);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.FixedWindow(permits: 100, window: TimeSpan.FromMinutes(1)));
        Assert.Contains("already been configured", ex.Message);
    }

    [Fact]
    public void GivenFixedWindowThenSlidingWindow_WhenCalled_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new RateLimitBuilder();
        builder.FixedWindow(permits: 100, window: TimeSpan.FromMinutes(1));

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.SlidingWindow(permits: 100, window: TimeSpan.FromMinutes(1), segmentsPerWindow: 4));
        Assert.Contains("already been configured", ex.Message);
    }

    // ── Build without configuration ──

    [Fact]
    public void GivenBuild_WhenNoAlgorithmConfigured_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new RateLimitBuilder();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build(queueLimit: 10));
        Assert.Contains("algorithm must be configured", ex.Message);
    }

    // ── Limiter behavior ──

    [Fact]
    public async Task GivenTokenBucket_WhenAcquiringWithinBurst_ThenPermitIsGranted()
    {
        // Arrange
        var builder = new RateLimitBuilder();
        builder.TokenBucket(permitsPerSecond: 100, burstSize: 10);
        using var limiter = builder.Build(queueLimit: 0);

        // Act
        using var lease = await limiter.AcquireAsync(1);

        // Assert
        Assert.True(lease.IsAcquired);
    }

    [Fact]
    public async Task GivenFixedWindow_WhenAcquiringWithinLimit_ThenPermitIsGranted()
    {
        // Arrange
        var builder = new RateLimitBuilder();
        builder.FixedWindow(permits: 10, window: TimeSpan.FromMinutes(1));
        using var limiter = builder.Build(queueLimit: 0);

        // Act
        using var lease = await limiter.AcquireAsync(1);

        // Assert
        Assert.True(lease.IsAcquired);
    }

    [Fact]
    public async Task GivenSlidingWindow_WhenAcquiringWithinLimit_ThenPermitIsGranted()
    {
        // Arrange
        var builder = new RateLimitBuilder();
        builder.SlidingWindow(permits: 10, window: TimeSpan.FromMinutes(1), segmentsPerWindow: 2);
        using var limiter = builder.Build(queueLimit: 0);

        // Act
        using var lease = await limiter.AcquireAsync(1);

        // Assert
        Assert.True(lease.IsAcquired);
    }
}

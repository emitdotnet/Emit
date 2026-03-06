namespace Emit.Abstractions.Tests;

using Xunit;

public sealed class BackoffTests
{
    [Theory]
    [InlineData(0, 1000)]
    [InlineData(1, 2000)]
    [InlineData(2, 4000)]
    [InlineData(3, 8000)]
    public void GivenExponentialWithoutJitter_WhenCalculateDelay_ThenReturnsExponentialGrowth(
        int attempt, long expectedMs)
    {
        // Arrange
        var backoff = Backoff.Exponential(TimeSpan.FromSeconds(1), jitter: false);

        // Act
        var delay = backoff.CalculateDelay(attempt);

        // Assert
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), delay);
    }

    [Fact]
    public void GivenExponentialWithJitter_WhenCalculateDelay_ThenReturnsValueWithinTwentyPercent()
    {
        // Arrange
        var initialDelay = TimeSpan.FromSeconds(1);
        var backoff = Backoff.Exponential(initialDelay, jitter: true);
        var baseDelay = TimeSpan.FromSeconds(1); // attempt 0

        // Act & Assert — run multiple times to verify jitter range
        for (var i = 0; i < 100; i++)
        {
            var delay = backoff.CalculateDelay(0);
            Assert.InRange(delay.TotalMilliseconds,
                baseDelay.TotalMilliseconds * 0.8,
                baseDelay.TotalMilliseconds * 1.2);
        }
    }

    [Fact]
    public void GivenHighAttemptNumber_WhenCalculateDelay_ThenCapsAtMaxDelay()
    {
        // Arrange
        var maxDelay = TimeSpan.FromSeconds(30);
        var backoff = Backoff.Exponential(TimeSpan.FromSeconds(1), jitter: false, maxDelay: maxDelay);

        // Act
        var delay = backoff.CalculateDelay(100);

        // Assert
        Assert.Equal(maxDelay, delay);
    }

    [Fact]
    public void GivenExponentialWithDefaultMaxDelay_WhenHighAttempt_ThenCapsAtFiveMinutes()
    {
        // Arrange
        var backoff = Backoff.Exponential(TimeSpan.FromSeconds(1), jitter: false);

        // Act
        var delay = backoff.CalculateDelay(50);

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(5), delay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public void GivenFixedBackoff_WhenCalculateDelay_ThenReturnsConstant(int attempt)
    {
        // Arrange
        var expected = TimeSpan.FromSeconds(3);
        var backoff = Backoff.Fixed(expected);

        // Act
        var delay = backoff.CalculateDelay(attempt);

        // Assert
        Assert.Equal(expected, delay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    public void GivenNoneBackoff_WhenCalculateDelay_ThenReturnsZero(int attempt)
    {
        // Arrange
        var backoff = Backoff.None;

        // Act
        var delay = backoff.CalculateDelay(attempt);

        // Assert
        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public void GivenNegativeInitialDelay_WhenCreateExponential_ThenThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Backoff.Exponential(TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public void GivenMaxDelayLessThanInitialDelay_WhenCreateExponential_ThenThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Backoff.Exponential(TimeSpan.FromSeconds(10), maxDelay: TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void GivenNegativeDelay_WhenCreateFixed_ThenThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Backoff.Fixed(TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public void GivenNoneBackoff_WhenAccessedMultipleTimes_ThenReturnsSameInstance()
    {
        // Act
        var a = Backoff.None;
        var b = Backoff.None;

        // Assert
        Assert.Same(a, b);
    }

    // ── Overflow / extreme attempt number tests ──

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(1_000_000)]
    [InlineData(10_000)]
    public void GivenExponentialWithoutJitter_WhenExcessiveAttemptNumber_ThenCapsAtMaxDelay(int attempt)
    {
        // Arrange
        var maxDelay = TimeSpan.FromMinutes(5);
        var backoff = Backoff.Exponential(TimeSpan.FromSeconds(1), jitter: false, maxDelay: maxDelay);

        // Act
        var delay = backoff.CalculateDelay(attempt);

        // Assert — must cap at maxDelay, never overflow or return garbage
        Assert.Equal(maxDelay, delay);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(1_000_000)]
    [InlineData(10_000)]
    public void GivenExponentialWithJitter_WhenExcessiveAttemptNumber_ThenCapsNearMaxDelay(int attempt)
    {
        // Arrange
        var maxDelay = TimeSpan.FromMinutes(5);
        var backoff = Backoff.Exponential(TimeSpan.FromSeconds(1), jitter: true, maxDelay: maxDelay);

        // Act
        var delay = backoff.CalculateDelay(attempt);

        // Assert — must be within jitter range of maxDelay (80%-120%), never negative or overflow
        Assert.True(delay >= TimeSpan.Zero, $"Delay must be non-negative, got {delay}");
        Assert.InRange(delay.TotalMilliseconds,
            maxDelay.TotalMilliseconds * 0.8,
            maxDelay.TotalMilliseconds * 1.2);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(1_000_000)]
    public void GivenExponentialWithZeroInitialDelay_WhenExcessiveAttemptNumber_ThenReturnsZero(int attempt)
    {
        // Arrange — zero initial delay: 0 * 2^N should always be 0, not NaN
        var backoff = Backoff.Exponential(TimeSpan.Zero, jitter: false, maxDelay: TimeSpan.FromMinutes(5));

        // Act
        var delay = backoff.CalculateDelay(attempt);

        // Assert
        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public void GivenNegativeAttemptNumber_WhenCalculateDelay_ThenReturnsNonNegativeDelay()
    {
        // Arrange
        var backoff = Backoff.Exponential(TimeSpan.FromSeconds(1), jitter: false);

        // Act
        var delay = backoff.CalculateDelay(-1);

        // Assert — 2^(-1) = 0.5, so delay should be 500ms (not negative or overflow)
        Assert.True(delay >= TimeSpan.Zero, $"Delay must be non-negative, got {delay}");
    }

    // ── ComputeTotalDelay tests ──

    [Fact]
    public void GivenFixedBackoff_WhenComputeTotalDelay_ThenReturnsDelayTimesAttempts()
    {
        // Arrange
        var backoff = Backoff.Fixed(TimeSpan.FromSeconds(5));

        // Act
        var total = backoff.ComputeTotalDelay(100);

        // Assert — 5s * 100 = 500s
        Assert.Equal(TimeSpan.FromSeconds(500), total);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(1_000_000)]
    public void GivenFixedBackoff_WhenExcessiveAttempts_ThenDoesNotOverflow(int attempts)
    {
        // Arrange
        var backoff = Backoff.Fixed(TimeSpan.FromSeconds(5));

        // Act
        var total = backoff.ComputeTotalDelay(attempts);

        // Assert
        Assert.True(total > TimeSpan.Zero);
    }

    [Fact]
    public void GivenExponentialWithoutJitter_WhenComputeTotalDelay_ThenReturnsGeometricSum()
    {
        // Arrange — 1s initial, no jitter, 5min max
        var backoff = Backoff.Exponential(TimeSpan.FromSeconds(1), jitter: false);

        // Act — 4 attempts: delays are 1s + 2s + 4s + 8s = 15s
        var total = backoff.ComputeTotalDelay(4);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(15), total);
    }

    [Fact]
    public void GivenExponentialWithoutJitter_WhenDelaysCapped_ThenUsesMaxDelayForRemainder()
    {
        // Arrange — 1s initial, 4s max, no jitter
        // Delays: attempt 0=1s, 1=2s, 2=4s(capped), 3=4s(capped)
        // Crossover at attempt 2: geometric sum = 1+2 = 3s, capped = 4s * 2 = 8s, total = 11s
        var backoff = Backoff.Exponential(TimeSpan.FromSeconds(1), jitter: false, maxDelay: TimeSpan.FromSeconds(4));

        // Act
        var total = backoff.ComputeTotalDelay(4);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(11), total);
    }

    [Fact]
    public void GivenExponentialWithJitter_WhenComputeTotalDelay_ThenReturnsWorstCase()
    {
        // Arrange
        var backoff = Backoff.Exponential(TimeSpan.FromSeconds(1), jitter: true);

        // Act — 4 attempts: base sum = 15s, worst-case jitter = 15s * 1.2 = 18s
        var total = backoff.ComputeTotalDelay(4);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(18), total);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(1_000_000)]
    public void GivenExponentialBackoff_WhenExcessiveAttempts_ThenReturnsFiniteValue(int attempts)
    {
        // Arrange
        var backoff = Backoff.Exponential(TimeSpan.FromSeconds(1), jitter: false);

        // Act
        var total = backoff.ComputeTotalDelay(attempts);

        // Assert — must not hang, must return a positive finite value
        Assert.True(total > TimeSpan.Zero);
        Assert.True(total <= TimeSpan.MaxValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void GivenAnyBackoff_WhenZeroOrNegativeAttempts_ThenReturnsZero(int attempts)
    {
        // Arrange
        var exponential = Backoff.Exponential(TimeSpan.FromSeconds(1), jitter: false);
        var @fixed = Backoff.Fixed(TimeSpan.FromSeconds(1));

        // Act & Assert
        Assert.Equal(TimeSpan.Zero, exponential.ComputeTotalDelay(attempts));
        Assert.Equal(TimeSpan.Zero, @fixed.ComputeTotalDelay(attempts));
        Assert.Equal(TimeSpan.Zero, Backoff.None.ComputeTotalDelay(attempts));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void GivenNoneBackoff_WhenComputeTotalDelay_ThenReturnsZero(int attempts)
    {
        // Act
        var total = Backoff.None.ComputeTotalDelay(attempts);

        // Assert
        Assert.Equal(TimeSpan.Zero, total);
    }

    [Fact]
    public void GivenExponentialWithZeroInitialDelay_WhenComputeTotalDelay_ThenReturnsZero()
    {
        // Arrange
        var backoff = Backoff.Exponential(TimeSpan.Zero, jitter: false, maxDelay: TimeSpan.FromMinutes(5));

        // Act
        var total = backoff.ComputeTotalDelay(1_000_000);

        // Assert
        Assert.Equal(TimeSpan.Zero, total);
    }
}

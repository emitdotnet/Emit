namespace Emit.Tests.Resilience;

using Emit.Resilience;
using Xunit;

public class ResiliencePolicyTests
{
    [Fact]
    public void GivenDefaultPolicy_WhenAccessing_ThenHasExpectedValues()
    {
        // Arrange & Act
        var policy = ResiliencePolicy.Default;

        // Assert
        Assert.Equal(10, policy.MaxRetryCount);
        Assert.Equal(BackoffStrategy.Exponential, policy.BackoffStrategy);
        Assert.Equal(TimeSpan.FromSeconds(2), policy.BackoffBaseDelay);
        Assert.Equal(TimeSpan.FromMinutes(5), policy.MaxBackoffDelay);
        Assert.Equal(5, policy.CircuitBreakerFailureThreshold);
        Assert.Equal(TimeSpan.FromMinutes(10), policy.CircuitBreakerCooldown);
    }

    [Theory]
    [InlineData(1, 2)]     // 2 seconds
    [InlineData(2, 4)]     // 4 seconds
    [InlineData(3, 8)]     // 8 seconds
    [InlineData(4, 16)]    // 16 seconds
    [InlineData(5, 32)]    // 32 seconds
    public void GivenExponentialBackoff_WhenCalculatingDelay_ThenDelayDoublesWithEachAttempt(
        int retryAttempt,
        int expectedSeconds)
    {
        // Arrange
        var policy = new ResiliencePolicy
        {
            MaxRetryCount = 10,
            BackoffStrategy = BackoffStrategy.Exponential,
            BackoffBaseDelay = TimeSpan.FromSeconds(2),
            MaxBackoffDelay = TimeSpan.FromMinutes(10),
            CircuitBreakerFailureThreshold = 5,
            CircuitBreakerCooldown = TimeSpan.FromMinutes(10)
        };

        // Act
        var delay = policy.CalculateBackoffDelay(retryAttempt);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    [Fact]
    public void GivenExponentialBackoff_WhenDelayExceedsMax_ThenReturnsCappedValue()
    {
        // Arrange
        var policy = new ResiliencePolicy
        {
            MaxRetryCount = 10,
            BackoffStrategy = BackoffStrategy.Exponential,
            BackoffBaseDelay = TimeSpan.FromSeconds(10),
            MaxBackoffDelay = TimeSpan.FromSeconds(30), // Cap at 30 seconds
            CircuitBreakerFailureThreshold = 5,
            CircuitBreakerCooldown = TimeSpan.FromMinutes(10)
        };

        // Act
        var delay = policy.CalculateBackoffDelay(5); // Would be 10 * 16 = 160 seconds

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(30), delay); // Capped
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public void GivenFixedIntervalBackoff_WhenCalculatingDelay_ThenDelayIsConstant(int retryAttempt)
    {
        // Arrange
        var baseDelay = TimeSpan.FromSeconds(5);
        var policy = new ResiliencePolicy
        {
            MaxRetryCount = 10,
            BackoffStrategy = BackoffStrategy.FixedInterval,
            BackoffBaseDelay = baseDelay,
            MaxBackoffDelay = TimeSpan.FromMinutes(10),
            CircuitBreakerFailureThreshold = 5,
            CircuitBreakerCooldown = TimeSpan.FromMinutes(10)
        };

        // Act
        var delay = policy.CalculateBackoffDelay(retryAttempt);

        // Assert
        Assert.Equal(baseDelay, delay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GivenInvalidRetryAttempt_WhenCalculatingDelay_ThenThrowsArgumentOutOfRangeException(int retryAttempt)
    {
        // Arrange
        var policy = ResiliencePolicy.Default;

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.CalculateBackoffDelay(retryAttempt));
    }
}

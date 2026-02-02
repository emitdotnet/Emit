namespace Emit.Tests.Resilience;

using Emit.Resilience;
using FluentValidation;
using Xunit;

public class ResiliencePolicyBuilderTests
{
    [Fact]
    public void GivenDefaultBuilder_WhenBuild_ThenReturnsDefaultPolicy()
    {
        // Arrange
        var builder = new ResiliencePolicyBuilder();

        // Act
        var policy = builder.Build();

        // Assert
        Assert.Equal(ResiliencePolicy.Default.MaxRetryCount, policy.MaxRetryCount);
        Assert.Equal(ResiliencePolicy.Default.BackoffStrategy, policy.BackoffStrategy);
        Assert.Equal(ResiliencePolicy.Default.BackoffBaseDelay, policy.BackoffBaseDelay);
        Assert.Equal(ResiliencePolicy.Default.MaxBackoffDelay, policy.MaxBackoffDelay);
        Assert.Equal(ResiliencePolicy.Default.CircuitBreakerFailureThreshold, policy.CircuitBreakerFailureThreshold);
        Assert.Equal(ResiliencePolicy.Default.CircuitBreakerCooldown, policy.CircuitBreakerCooldown);
    }

    [Fact]
    public void GivenWithRetry_WhenBuild_ThenPolicyHasRetrySettings()
    {
        // Arrange
        var builder = new ResiliencePolicyBuilder()
            .WithRetry(
                maxRetries: 5,
                BackoffStrategy.FixedInterval,
                baseDelay: TimeSpan.FromSeconds(3),
                maxDelay: TimeSpan.FromMinutes(1));

        // Act
        var policy = builder.Build();

        // Assert
        Assert.Equal(5, policy.MaxRetryCount);
        Assert.Equal(BackoffStrategy.FixedInterval, policy.BackoffStrategy);
        Assert.Equal(TimeSpan.FromSeconds(3), policy.BackoffBaseDelay);
        Assert.Equal(TimeSpan.FromMinutes(1), policy.MaxBackoffDelay);
    }

    [Fact]
    public void GivenWithCircuitBreaker_WhenBuild_ThenPolicyHasCircuitBreakerSettings()
    {
        // Arrange
        var builder = new ResiliencePolicyBuilder()
            .WithCircuitBreaker(failureThreshold: 3, cooldown: TimeSpan.FromMinutes(5));

        // Act
        var policy = builder.Build();

        // Assert
        Assert.Equal(3, policy.CircuitBreakerFailureThreshold);
        Assert.Equal(TimeSpan.FromMinutes(5), policy.CircuitBreakerCooldown);
    }

    [Fact]
    public void GivenFluentChaining_WhenBuild_ThenAllSettingsApplied()
    {
        // Arrange
        var builder = new ResiliencePolicyBuilder()
            .WithRetry(7, BackoffStrategy.Exponential, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(2))
            .WithCircuitBreaker(4, TimeSpan.FromMinutes(8));

        // Act
        var policy = builder.Build();

        // Assert
        Assert.Equal(7, policy.MaxRetryCount);
        Assert.Equal(BackoffStrategy.Exponential, policy.BackoffStrategy);
        Assert.Equal(TimeSpan.FromSeconds(1), policy.BackoffBaseDelay);
        Assert.Equal(TimeSpan.FromMinutes(2), policy.MaxBackoffDelay);
        Assert.Equal(4, policy.CircuitBreakerFailureThreshold);
        Assert.Equal(TimeSpan.FromMinutes(8), policy.CircuitBreakerCooldown);
    }

    [Fact]
    public void GivenNegativeMaxRetries_WhenBuild_ThenThrowsValidationException()
    {
        // Arrange
        var builder = new ResiliencePolicyBuilder()
            .WithRetry(-1, BackoffStrategy.Exponential, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(5));

        // Act & Assert
        var exception = Assert.Throws<ValidationException>(() => builder.Build());
        Assert.Contains("Max retry count must be non-negative", exception.Message);
    }

    [Fact]
    public void GivenBaseDelayLessThanMinimum_WhenBuild_ThenThrowsValidationException()
    {
        // Arrange
        var builder = new ResiliencePolicyBuilder()
            .WithRetry(5, BackoffStrategy.Exponential, TimeSpan.FromMilliseconds(500), TimeSpan.FromMinutes(5));

        // Act & Assert
        var exception = Assert.Throws<ValidationException>(() => builder.Build());
        Assert.Contains("Backoff base delay must be at least", exception.Message);
    }

    [Fact]
    public void GivenMaxDelayLessThanBaseDelay_WhenBuild_ThenThrowsValidationException()
    {
        // Arrange
        var builder = new ResiliencePolicyBuilder()
            .WithRetry(5, BackoffStrategy.Exponential, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));

        // Act & Assert
        var exception = Assert.Throws<ValidationException>(() => builder.Build());
        Assert.Contains("Max backoff delay must be greater than base delay", exception.Message);
    }

    [Fact]
    public void GivenZeroFailureThreshold_WhenBuild_ThenThrowsValidationException()
    {
        // Arrange
        var builder = new ResiliencePolicyBuilder()
            .WithCircuitBreaker(0, TimeSpan.FromMinutes(5));

        // Act & Assert
        var exception = Assert.Throws<ValidationException>(() => builder.Build());
        Assert.Contains("Circuit breaker failure threshold must be positive", exception.Message);
    }

    [Fact]
    public void GivenCooldownLessThanMinimum_WhenBuild_ThenThrowsValidationException()
    {
        // Arrange
        var builder = new ResiliencePolicyBuilder()
            .WithCircuitBreaker(5, TimeSpan.FromSeconds(10));

        // Act & Assert
        var exception = Assert.Throws<ValidationException>(() => builder.Build());
        Assert.Contains("Circuit breaker cooldown must be at least", exception.Message);
    }

    [Fact]
    public void GivenZeroRetries_WhenBuild_ThenSucceeds()
    {
        // Arrange
        var builder = new ResiliencePolicyBuilder()
            .WithRetry(0, BackoffStrategy.Exponential, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));

        // Act
        var policy = builder.Build();

        // Assert
        Assert.Equal(0, policy.MaxRetryCount);
    }
}

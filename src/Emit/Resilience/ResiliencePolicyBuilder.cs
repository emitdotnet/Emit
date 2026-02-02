namespace Emit.Resilience;

using FluentValidation;

/// <summary>
/// Fluent builder for configuring <see cref="ResiliencePolicy"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// Use this builder to configure retry and circuit breaker behavior for outbox processing.
/// All configuration values are validated when <see cref="Build"/> is called.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// var policy = new ResiliencePolicyBuilder()
///     .WithRetry(maxRetries: 5, BackoffStrategy.Exponential,
///                baseDelay: TimeSpan.FromSeconds(1),
///                maxDelay: TimeSpan.FromMinutes(2))
///     .WithCircuitBreaker(failureThreshold: 3,
///                         cooldown: TimeSpan.FromMinutes(5))
///     .Build();
/// </code>
/// </para>
/// </remarks>
public sealed class ResiliencePolicyBuilder
{
    private int maxRetryCount = ResiliencePolicy.Default.MaxRetryCount;
    private BackoffStrategy backoffStrategy = ResiliencePolicy.Default.BackoffStrategy;
    private TimeSpan backoffBaseDelay = ResiliencePolicy.Default.BackoffBaseDelay;
    private TimeSpan maxBackoffDelay = ResiliencePolicy.Default.MaxBackoffDelay;
    private int circuitBreakerFailureThreshold = ResiliencePolicy.Default.CircuitBreakerFailureThreshold;
    private TimeSpan circuitBreakerCooldown = ResiliencePolicy.Default.CircuitBreakerCooldown;

    /// <summary>
    /// Configures the retry behavior.
    /// </summary>
    /// <param name="maxRetries">Maximum number of retry attempts. Must be non-negative.</param>
    /// <param name="strategy">The backoff strategy to use between retries.</param>
    /// <param name="baseDelay">The base delay between retries. Minimum: 1 second.</param>
    /// <param name="maxDelay">The maximum delay between retries. Must be greater than <paramref name="baseDelay"/>.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public ResiliencePolicyBuilder WithRetry(
        int maxRetries,
        BackoffStrategy strategy,
        TimeSpan baseDelay,
        TimeSpan maxDelay)
    {
        maxRetryCount = maxRetries;
        backoffStrategy = strategy;
        backoffBaseDelay = baseDelay;
        maxBackoffDelay = maxDelay;
        return this;
    }

    /// <summary>
    /// Configures the circuit breaker behavior.
    /// </summary>
    /// <param name="failureThreshold">Number of consecutive failures before tripping. Must be positive.</param>
    /// <param name="cooldown">Duration before attempting again after circuit trips. Minimum: 30 seconds.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public ResiliencePolicyBuilder WithCircuitBreaker(int failureThreshold, TimeSpan cooldown)
    {
        circuitBreakerFailureThreshold = failureThreshold;
        circuitBreakerCooldown = cooldown;
        return this;
    }

    /// <summary>
    /// Builds and validates the <see cref="ResiliencePolicy"/>.
    /// </summary>
    /// <returns>A validated, immutable <see cref="ResiliencePolicy"/> instance.</returns>
    /// <exception cref="ValidationException">Thrown when the configuration is invalid.</exception>
    public ResiliencePolicy Build()
    {
        var policy = new ResiliencePolicy
        {
            MaxRetryCount = maxRetryCount,
            BackoffStrategy = backoffStrategy,
            BackoffBaseDelay = backoffBaseDelay,
            MaxBackoffDelay = maxBackoffDelay,
            CircuitBreakerFailureThreshold = circuitBreakerFailureThreshold,
            CircuitBreakerCooldown = circuitBreakerCooldown
        };

        var validator = new ResiliencePolicyValidator();
        validator.ValidateAndThrow(policy);

        return policy;
    }
}

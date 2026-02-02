namespace Emit.Resilience;

/// <summary>
/// Defines retry and circuit breaker configuration for outbox processing.
/// </summary>
/// <remarks>
/// <para>
/// The resilience policy configures how the outbox worker handles failures during
/// entry processing. It includes both retry logic (how many times to retry and how
/// long to wait between attempts) and circuit breaker logic (when to stop trying
/// entirely for a group).
/// </para>
/// <para>
/// Policies can be configured at multiple levels with the following override hierarchy:
/// <list type="number">
/// <item><description>Producer-level (most specific)</description></item>
/// <item><description>Provider-level</description></item>
/// <item><description>Global-level</description></item>
/// <item><description>Built-in default</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class ResiliencePolicy
{
    /// <summary>
    /// Gets the built-in default resilience policy.
    /// </summary>
    /// <remarks>
    /// Default values:
    /// <list type="bullet">
    /// <item><description>Max retry count: 10</description></item>
    /// <item><description>Backoff strategy: Exponential</description></item>
    /// <item><description>Backoff base delay: 2 seconds</description></item>
    /// <item><description>Max backoff delay: 5 minutes</description></item>
    /// <item><description>Circuit breaker failure threshold: 5 consecutive failures</description></item>
    /// <item><description>Circuit breaker cooldown: 10 minutes</description></item>
    /// </list>
    /// </remarks>
    public static ResiliencePolicy Default { get; } = new()
    {
        MaxRetryCount = 10,
        BackoffStrategy = BackoffStrategy.Exponential,
        BackoffBaseDelay = TimeSpan.FromSeconds(2),
        MaxBackoffDelay = TimeSpan.FromMinutes(5),
        CircuitBreakerFailureThreshold = 5,
        CircuitBreakerCooldown = TimeSpan.FromMinutes(10)
    };

    /// <summary>
    /// Gets or sets the maximum number of retry attempts before marking an entry as permanently failed.
    /// </summary>
    /// <remarks>
    /// A value of 0 means no retries - failures are immediately permanent.
    /// </remarks>
    public required int MaxRetryCount { get; init; }

    /// <summary>
    /// Gets or sets the backoff strategy for calculating delays between retry attempts.
    /// </summary>
    public required BackoffStrategy BackoffStrategy { get; init; }

    /// <summary>
    /// Gets or sets the base delay for retry backoff calculations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For <see cref="Resilience.BackoffStrategy.Exponential"/>: This is the initial delay,
    /// which doubles with each retry attempt.
    /// </para>
    /// <para>
    /// For <see cref="Resilience.BackoffStrategy.FixedInterval"/>: This is the constant delay
    /// between all retry attempts.
    /// </para>
    /// <para>
    /// Minimum value: 1 second.
    /// </para>
    /// </remarks>
    public required TimeSpan BackoffBaseDelay { get; init; }

    /// <summary>
    /// Gets or sets the maximum delay between retry attempts.
    /// </summary>
    /// <remarks>
    /// The backoff delay is capped at this value regardless of the backoff strategy.
    /// Must be greater than <see cref="BackoffBaseDelay"/>.
    /// </remarks>
    public required TimeSpan MaxBackoffDelay { get; init; }

    /// <summary>
    /// Gets or sets the number of consecutive failures before the circuit breaker trips.
    /// </summary>
    /// <remarks>
    /// When a group fails this many times consecutively, the circuit breaker opens
    /// and the group is skipped for the duration of <see cref="CircuitBreakerCooldown"/>.
    /// </remarks>
    public required int CircuitBreakerFailureThreshold { get; init; }

    /// <summary>
    /// Gets or sets how long the circuit breaker remains open before moving to half-open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// After this duration, the circuit moves to half-open state where one entry is
    /// attempted. If it succeeds, the circuit closes. If it fails, the circuit reopens.
    /// </para>
    /// <para>
    /// Minimum value: 30 seconds.
    /// </para>
    /// </remarks>
    public required TimeSpan CircuitBreakerCooldown { get; init; }

    /// <summary>
    /// Calculates the delay before the next retry attempt.
    /// </summary>
    /// <param name="retryAttempt">The retry attempt number (1-based).</param>
    /// <returns>The delay before the retry attempt.</returns>
    public TimeSpan CalculateBackoffDelay(int retryAttempt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retryAttempt);

        var delay = BackoffStrategy switch
        {
            BackoffStrategy.Exponential => TimeSpan.FromTicks(
                BackoffBaseDelay.Ticks * (long)Math.Pow(2, retryAttempt - 1)),
            BackoffStrategy.FixedInterval => BackoffBaseDelay,
            _ => BackoffBaseDelay
        };

        return delay > MaxBackoffDelay ? MaxBackoffDelay : delay;
    }
}

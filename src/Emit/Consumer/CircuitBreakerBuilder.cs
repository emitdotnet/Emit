namespace Emit.Consumer;

/// <summary>
/// Configures a circuit breaker for a consumer group. When the failure rate exceeds
/// the configured threshold within the sampling window, the circuit opens and pauses
/// all consumers in the group. After the pause duration, a single message is allowed
/// through (half-open state): success closes the circuit, failure re-opens it.
/// </summary>
public sealed class CircuitBreakerBuilder
{
    private int failureThreshold;
    private TimeSpan samplingWindow;
    private TimeSpan pauseDuration;
    private readonly List<Type> tripOnExceptionTypes = [];
    private bool configured;

    /// <summary>
    /// Sets the number of failures within the sampling window that triggers the circuit to open.
    /// </summary>
    /// <param name="threshold">The failure count threshold. Must be greater than zero.</param>
    /// <returns>This builder for chaining.</returns>
    public CircuitBreakerBuilder FailureThreshold(int threshold)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(threshold);
        failureThreshold = threshold;
        configured = true;
        return this;
    }

    /// <summary>
    /// Sets the time window over which failures are counted toward the threshold.
    /// </summary>
    /// <param name="window">The sampling window duration. Must be greater than zero.</param>
    /// <returns>This builder for chaining.</returns>
    public CircuitBreakerBuilder SamplingWindow(TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        samplingWindow = window;
        return this;
    }

    /// <summary>
    /// Sets how long the circuit remains open before transitioning to half-open.
    /// During this period, all consumers in the group are paused.
    /// </summary>
    /// <param name="duration">The pause duration. Must be greater than zero.</param>
    /// <returns>This builder for chaining.</returns>
    public CircuitBreakerBuilder PauseDuration(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        pauseDuration = duration;
        return this;
    }

    /// <summary>
    /// Restricts which exception types count toward the failure threshold.
    /// Only exceptions assignable to <typeparamref name="TException"/> will increment
    /// the failure counter. If no <c>TripOn</c> is configured, all exceptions count.
    /// Multiple calls are additive.
    /// </summary>
    /// <typeparam name="TException">The exception type to count.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public CircuitBreakerBuilder TripOn<TException>() where TException : Exception
    {
        tripOnExceptionTypes.Add(typeof(TException));
        return this;
    }

    /// <summary>
    /// Builds the circuit breaker configuration. Called internally during consumer group registration.
    /// </summary>
    /// <returns>The circuit breaker configuration.</returns>
    /// <exception cref="InvalidOperationException">Required properties were not configured.</exception>
    public CircuitBreakerConfig Build()
    {
        if (!configured || failureThreshold <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(FailureThreshold)} must be configured.");
        }

        if (samplingWindow <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(SamplingWindow)} must be configured.");
        }

        if (pauseDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(PauseDuration)} must be configured.");
        }

        return new CircuitBreakerConfig(
            failureThreshold,
            samplingWindow,
            pauseDuration,
            tripOnExceptionTypes.Count > 0 ? tripOnExceptionTypes.ToArray() : null);
    }
}

/// <summary>
/// Immutable configuration for the circuit breaker.
/// </summary>
public sealed record CircuitBreakerConfig(
    int FailureThreshold,
    TimeSpan SamplingWindow,
    TimeSpan PauseDuration,
    Type[]? TripOnExceptionTypes);

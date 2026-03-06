namespace Emit.Abstractions;

/// <summary>
/// Defines a backoff strategy for calculating delays between retry attempts.
/// Use the static factory methods to create a strategy.
/// </summary>
public abstract class Backoff
{
    private static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A backoff strategy that returns no delay between attempts.
    /// </summary>
    public static Backoff None { get; } = new NoneBackoff();

    private Backoff()
    {
    }

    /// <summary>
    /// Creates an exponential backoff strategy where the delay doubles with each attempt.
    /// </summary>
    /// <param name="initialDelay">The delay for the first retry attempt. Must be non-negative.</param>
    /// <param name="jitter">
    /// When <c>true</c>, applies random jitter of ±20% to each calculated delay
    /// to prevent thundering herd problems.
    /// </param>
    /// <param name="maxDelay">
    /// The maximum delay cap. Defaults to 5 minutes.
    /// Must be greater than or equal to <paramref name="initialDelay"/>.
    /// </param>
    /// <returns>A new exponential backoff strategy.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="initialDelay"/> is negative, or <paramref name="maxDelay"/> is less than <paramref name="initialDelay"/>.
    /// </exception>
    public static Backoff Exponential(TimeSpan initialDelay, bool jitter = true, TimeSpan? maxDelay = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(initialDelay, TimeSpan.Zero, nameof(initialDelay));
        var effectiveMaxDelay = maxDelay ?? DefaultMaxDelay;
        ArgumentOutOfRangeException.ThrowIfLessThan(effectiveMaxDelay, initialDelay, nameof(maxDelay));

        return new ExponentialBackoff(initialDelay, jitter, effectiveMaxDelay);
    }

    /// <summary>
    /// Creates a fixed backoff strategy that returns the same delay for every attempt.
    /// </summary>
    /// <param name="delay">The constant delay between attempts. Must be non-negative.</param>
    /// <returns>A new fixed backoff strategy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/> is negative.</exception>
    public static Backoff Fixed(TimeSpan delay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero, nameof(delay));

        return new FixedBackoff(delay);
    }

    /// <summary>
    /// Calculates the delay for the given attempt number.
    /// </summary>
    /// <param name="attemptNumber">The zero-based attempt number.</param>
    /// <returns>The delay to wait before the next attempt.</returns>
    public abstract TimeSpan CalculateDelay(int attemptNumber);

    /// <summary>
    /// Computes the worst-case total delay across the given number of attempts.
    /// For strategies with jitter, returns the upper bound (maximum jitter factor applied).
    /// </summary>
    /// <param name="attempts">The number of retry attempts.</param>
    /// <returns>
    /// The worst-case total delay, capped at <see cref="TimeSpan.MaxValue"/> to prevent overflow.
    /// Returns <see cref="TimeSpan.Zero"/> when <paramref name="attempts"/> is zero or negative.
    /// </returns>
    public abstract TimeSpan ComputeTotalDelay(int attempts);

    private sealed class ExponentialBackoff(TimeSpan initialDelay, bool jitter, TimeSpan maxDelay) : Backoff
    {
        public override TimeSpan CalculateDelay(int attemptNumber)
        {
            if (initialDelay == TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            var delayTicks = Math.Min(
                initialDelay.Ticks * Math.Pow(2, attemptNumber),
                maxDelay.Ticks);

            var delay = TimeSpan.FromTicks((long)delayTicks);

            if (jitter)
            {
                var jitterFactor = 0.8 + (Random.Shared.NextDouble() * 0.4);
                delay = TimeSpan.FromTicks((long)(delay.Ticks * jitterFactor));
            }

            return delay;
        }

        public override TimeSpan ComputeTotalDelay(int attempts)
        {
            if (attempts <= 0 || initialDelay == TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            // Find the crossover attempt where delay reaches maxDelay.
            // Before crossover: geometric growth (initialDelay * 2^i).
            // At and after crossover: capped at maxDelay.
            var crossover = Math.Ceiling(Math.Log2((double)maxDelay.Ticks / initialDelay.Ticks));

            double totalTicks;

            if (crossover >= attempts)
            {
                // All attempts are in the geometric growth phase.
                // Sum = initialDelay * (2^attempts - 1)
                totalTicks = initialDelay.Ticks * (Math.Pow(2, attempts) - 1);
            }
            else
            {
                var geometricCount = Math.Max((int)crossover, 0);
                totalTicks = initialDelay.Ticks * (Math.Pow(2, geometricCount) - 1)
                    + (double)maxDelay.Ticks * (attempts - geometricCount);
            }

            if (jitter)
            {
                totalTicks *= 1.2;
            }

            return double.IsInfinity(totalTicks) || totalTicks >= long.MaxValue
                ? TimeSpan.MaxValue
                : TimeSpan.FromTicks((long)totalTicks);
        }
    }

    private sealed class FixedBackoff(TimeSpan delay) : Backoff
    {
        public override TimeSpan CalculateDelay(int attemptNumber) => delay;

        public override TimeSpan ComputeTotalDelay(int attempts)
        {
            if (attempts <= 0)
            {
                return TimeSpan.Zero;
            }

            var totalTicks = (double)delay.Ticks * attempts;

            return totalTicks >= long.MaxValue
                ? TimeSpan.MaxValue
                : TimeSpan.FromTicks((long)totalTicks);
        }
    }

    private sealed class NoneBackoff : Backoff
    {
        public override TimeSpan CalculateDelay(int attemptNumber) => TimeSpan.Zero;

        public override TimeSpan ComputeTotalDelay(int attempts) => TimeSpan.Zero;
    }
}

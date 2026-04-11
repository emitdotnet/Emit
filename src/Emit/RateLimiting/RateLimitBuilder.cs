namespace Emit.RateLimiting;

using System.Threading.RateLimiting;

/// <summary>
/// Configures a rate limiter for throttling inbound message processing. Supports token bucket,
/// fixed window, and sliding window algorithms using the BCL <see cref="RateLimiter"/> types.
/// Exactly one algorithm must be configured.
/// </summary>
public sealed class RateLimitBuilder
{
    private Func<int, RateLimiter>? factory;

    /// <summary>
    /// Configures a token bucket rate limiter that replenishes tokens at a steady rate.
    /// Allows bursts up to <paramref name="burstSize"/> while maintaining a long-term
    /// average rate of <paramref name="permitsPerSecond"/> messages per second.
    /// </summary>
    /// <param name="permitsPerSecond">Tokens replenished per second.</param>
    /// <param name="burstSize">Maximum tokens available at any time.</param>
    /// <returns>This builder for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="permitsPerSecond"/> or <paramref name="burstSize"/> is zero or negative.
    /// </exception>
    public RateLimitBuilder TokenBucket(int permitsPerSecond, int burstSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(permitsPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(burstSize);
        EnsureNotAlreadyConfigured();

        factory = queueLimit => new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = burstSize,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            TokensPerPeriod = permitsPerSecond,
            QueueLimit = queueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });

        return this;
    }

    /// <summary>
    /// Configures a fixed window rate limiter that allows a fixed number of permits
    /// within each time window. The window resets completely when the period expires.
    /// </summary>
    /// <param name="permits">Maximum permits per window.</param>
    /// <param name="window">Duration of each time window.</param>
    /// <returns>This builder for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="permits"/> is zero or negative, or <paramref name="window"/> is zero or negative.
    /// </exception>
    public RateLimitBuilder FixedWindow(int permits, TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(permits);
        ThrowIfInvalidWindow(window);
        EnsureNotAlreadyConfigured();

        factory = queueLimit => new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = window,
            QueueLimit = queueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });

        return this;
    }

    /// <summary>
    /// Configures a sliding window rate limiter that distributes permits across
    /// segments within a window. Provides smoother throttling than fixed window
    /// by gradually releasing permits as segments rotate.
    /// </summary>
    /// <param name="permits">Maximum permits across the full window.</param>
    /// <param name="window">Duration of the sliding window.</param>
    /// <param name="segmentsPerWindow">Number of segments the window is divided into.</param>
    /// <returns>This builder for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="permits"/> is zero or negative, <paramref name="window"/> is zero or negative,
    /// or <paramref name="segmentsPerWindow"/> is zero or negative.
    /// </exception>
    public RateLimitBuilder SlidingWindow(int permits, TimeSpan window, int segmentsPerWindow)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(permits);
        ThrowIfInvalidWindow(window);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(segmentsPerWindow);
        EnsureNotAlreadyConfigured();

        factory = queueLimit => new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = window,
            SegmentsPerWindow = segmentsPerWindow,
            QueueLimit = queueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });

        return this;
    }

    /// <summary>
    /// Builds the <see cref="RateLimiter"/> with the specified queue limit.
    /// The queue limit determines how many acquire requests can wait for a permit
    /// before being rejected.
    /// </summary>
    /// <param name="queueLimit">Maximum number of queued acquire requests.</param>
    /// <returns>The configured rate limiter.</returns>
    /// <exception cref="InvalidOperationException">No rate limiting algorithm was configured.</exception>
    public RateLimiter Build(int queueLimit)
    {
        if (factory is null)
        {
            throw new InvalidOperationException(
                $"A rate limiting algorithm must be configured. Call {nameof(TokenBucket)}, {nameof(FixedWindow)}, or {nameof(SlidingWindow)}.");
        }

        return factory(queueLimit);
    }

    private void EnsureNotAlreadyConfigured()
    {
        if (factory is not null)
        {
            throw new InvalidOperationException(
                "A rate limiting algorithm has already been configured. Only one algorithm can be specified.");
        }
    }

    private static void ThrowIfInvalidWindow(TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
    }
}

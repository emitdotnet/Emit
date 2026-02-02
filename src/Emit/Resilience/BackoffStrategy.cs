namespace Emit.Resilience;

/// <summary>
/// Specifies the backoff strategy for retry delays.
/// </summary>
public enum BackoffStrategy
{
    /// <summary>
    /// Exponential backoff with configurable base delay.
    /// </summary>
    /// <remarks>
    /// The delay doubles with each retry attempt up to the maximum delay.
    /// Formula: min(baseDelay * 2^(retryCount-1), maxDelay)
    /// </remarks>
    Exponential = 0,

    /// <summary>
    /// Fixed interval between retries.
    /// </summary>
    /// <remarks>
    /// The delay remains constant for all retry attempts.
    /// </remarks>
    FixedInterval = 1
}

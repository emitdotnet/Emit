namespace Emit.Pipeline.Modules;

using Emit.Abstractions;

/// <summary>
/// Holds retry configuration. Separate from error policy — retry is a
/// consume-pipeline concern.
/// </summary>
public sealed class RetryModule
{
    private int? maxAttempts;
    private Backoff? backoff;

    /// <summary>
    /// Gets whether retry has been configured.
    /// </summary>
    public bool IsConfigured => maxAttempts.HasValue;

    /// <summary>
    /// Configures retry with the given max attempts and backoff strategy.
    /// </summary>
    /// <param name="maxAttempts">Maximum number of retry attempts. Must be greater than zero.</param>
    /// <param name="backoff">The backoff strategy between retries.</param>
    /// <exception cref="InvalidOperationException">Retry has already been configured.</exception>
    public void Configure(int maxAttempts, Backoff backoff)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxAttempts, 0, nameof(maxAttempts));
        ArgumentNullException.ThrowIfNull(backoff);

        if (IsConfigured)
        {
            throw new InvalidOperationException("Retry has already been configured.");
        }

        this.maxAttempts = maxAttempts;
        this.backoff = backoff;
    }

    /// <summary>
    /// Returns the retry config, or <c>null</c> if not configured.
    /// </summary>
    internal RetryConfig? BuildConfig()
    {
        if (!IsConfigured)
        {
            return null;
        }

        return new RetryConfig(maxAttempts!.Value, backoff!);
    }
}

/// <summary>
/// Immutable retry configuration.
/// </summary>
/// <param name="MaxAttempts">Maximum number of retry attempts.</param>
/// <param name="Backoff">Backoff strategy between retries.</param>
public sealed record RetryConfig(int MaxAttempts, Backoff Backoff);

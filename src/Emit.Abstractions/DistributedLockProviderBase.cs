namespace Emit.Abstractions;

using System.Diagnostics;
using Emit.Abstractions.Metrics;

/// <summary>
/// Base class for distributed lock providers. Handles retry logic with exponential backoff
/// and lock handle lifecycle.
/// </summary>
public abstract class DistributedLockProviderBase : IDistributedLockProvider
{
    private static readonly TimeSpan RetryBaseInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan RetryMaxInterval = TimeSpan.FromSeconds(5);

    private readonly IRandomProvider randomProvider;
    private readonly TimeProvider timeProvider;
    private readonly LockMetrics? lockMetrics;

    /// <summary>
    /// Initializes a new instance of the <see cref="DistributedLockProviderBase"/> class.
    /// </summary>
    /// <param name="randomProvider">Random number provider for jitter calculation.</param>
    /// <param name="timeProvider">Time provider for delays and timeouts. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="lockMetrics">Optional lock metrics for instrumentation.</param>
    protected DistributedLockProviderBase(
        IRandomProvider randomProvider,
        TimeProvider? timeProvider = null,
        LockMetrics? lockMetrics = null)
    {
        ArgumentNullException.ThrowIfNull(randomProvider);
        this.randomProvider = randomProvider;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.lockMetrics = lockMetrics;
    }

    /// <inheritdoc />
    public async Task<IDistributedLock?> TryAcquireAsync(
        string key,
        TimeSpan ttl,
        TimeSpan timeout = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);

        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be non-negative or Timeout.InfiniteTimeSpan.");
        }

        var lockId = Guid.NewGuid();
        var attempt = 0;
        var startTicks = lockMetrics is not null ? Stopwatch.GetTimestamp() : 0;
        string? acquireResult = null;

        using var timeoutCts = timeout != Timeout.InfiniteTimeSpan && timeout > TimeSpan.Zero
            ? new CancellationTokenSource(timeout, timeProvider)
            : null;

        using var linkedCts = timeoutCts is not null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token)
            : null;

        var effectiveToken = linkedCts?.Token ?? cancellationToken;

        try
        {
            while (true)
            {
                if (await TryAcquireCoreAsync(key, lockId, ttl, cancellationToken).ConfigureAwait(false))
                {
                    acquireResult = "acquired";
                    return new DistributedLock(this, key, lockId, lockMetrics);
                }

                // Single-attempt mode (timeout == default/Zero)
                if (timeout == TimeSpan.Zero)
                {
                    acquireResult = "timeout";
                    return null;
                }

                // Check if we should keep retrying
                if (effectiveToken.IsCancellationRequested)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        acquireResult = "cancelled";
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    acquireResult = "timeout";
                    return null;
                }

                var delay = CalculateBackoffDelay(attempt);
                attempt++;

                try
                {
                    await Task.Delay(delay, timeProvider, effectiveToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Timeout expired during delay — return null
                    acquireResult = "timeout";
                    return null;
                }
            }
        }
        catch (OperationCanceledException) when (acquireResult is null)
        {
            acquireResult = "cancelled";
            throw;
        }
        finally
        {
            if (lockMetrics is not null && acquireResult is not null)
            {
                lockMetrics.RecordAcquireDuration(
                    Stopwatch.GetElapsedTime(startTicks).TotalSeconds, acquireResult);
                lockMetrics.RecordAcquireRetries(attempt);
            }
        }
    }

    /// <summary>
    /// Attempts to acquire the lock in the underlying store.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <param name="lockId">Unique identifier for this acquisition.</param>
    /// <param name="ttl">Time-to-live for the lock.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the lock was acquired.</returns>
    protected abstract Task<bool> TryAcquireCoreAsync(
        string key,
        Guid lockId,
        TimeSpan ttl,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases the lock in the underlying store.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <param name="lockId">The lock identifier from acquisition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected abstract Task ReleaseCoreAsync(
        string key,
        Guid lockId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resets the lock's expiration to <paramref name="ttl"/> from the current server time in the underlying store.
    /// This replaces the existing expiration — it does not add to the remaining time.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <param name="lockId">The lock identifier from acquisition.</param>
    /// <param name="ttl">Duration from the current server time until the lock expires.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the lock was extended; <c>false</c> if the lock was lost.</returns>
    protected abstract Task<bool> ExtendCoreAsync(
        string key,
        Guid lockId,
        TimeSpan ttl,
        CancellationToken cancellationToken);

    internal async Task ReleaseLockAsync(string key, Guid lockId, CancellationToken cancellationToken)
    {
        await ReleaseCoreAsync(key, lockId, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<bool> ExtendLockAsync(string key, Guid lockId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        return await ExtendCoreAsync(key, lockId, ttl, cancellationToken).ConfigureAwait(false);
    }

    private TimeSpan CalculateBackoffDelay(int attempt)
    {
        var baseMs = RetryBaseInterval.TotalMilliseconds * Math.Pow(2, attempt);
        var cappedMs = Math.Min(baseMs, RetryMaxInterval.TotalMilliseconds);

        // Jitter: ±25% (multiply by 0.75 to 1.25)
        var jitter = 0.75 + (randomProvider.NextDouble() * 0.5);
        var delayMs = cappedMs * jitter;

        return TimeSpan.FromMilliseconds(delayMs);
    }
}

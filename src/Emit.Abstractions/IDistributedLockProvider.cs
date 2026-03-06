namespace Emit.Abstractions;

/// <summary>
/// Provides distributed lock acquisition.
/// </summary>
public interface IDistributedLockProvider
{
    /// <summary>
    /// Attempts to acquire a distributed lock on the specified key.
    /// </summary>
    /// <param name="key">The resource key to lock.</param>
    /// <param name="ttl">
    /// Time-to-live for the lock. The lock expires automatically after this duration
    /// unless extended via <see cref="IDistributedLock.ExtendAsync"/>.
    /// </param>
    /// <param name="timeout">
    /// Maximum time to wait for lock acquisition. <see cref="TimeSpan.Zero"/> (default)
    /// attempts exactly once. Use <see cref="Timeout.InfiniteTimeSpan"/> to retry
    /// until acquired or cancelled.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An <see cref="IDistributedLock"/> handle if acquired; <c>null</c> if the lock
    /// could not be acquired within the timeout.
    /// </returns>
    Task<IDistributedLock?> TryAcquireAsync(
        string key,
        TimeSpan ttl,
        TimeSpan timeout = default,
        CancellationToken cancellationToken = default);
}

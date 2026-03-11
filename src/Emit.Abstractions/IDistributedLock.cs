namespace Emit.Abstractions;

/// <summary>
/// Represents an acquired distributed lock.
/// </summary>
/// <remarks>
/// Disposing the handle releases the lock. The lock is also released if the TTL expires
/// without renewal via <see cref="ExtendAsync"/>.
/// </remarks>
public interface IDistributedLock : IAsyncDisposable
{
    /// <summary>
    /// Gets the key of the locked resource.
    /// </summary>
    string Key { get; }

    /// <summary>
    /// Resets the lock's expiration to <paramref name="ttl"/> from the current server time.
    /// This replaces the existing expiration — it does not add to the remaining time.
    /// </summary>
    /// <param name="ttl">Duration from the current server time until the lock expires.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the lock was extended; <c>false</c> if the lock was already lost.</returns>
    /// <exception cref="ObjectDisposedException">The handle has been disposed.</exception>
    Task<bool> ExtendAsync(TimeSpan ttl, CancellationToken cancellationToken = default);
}

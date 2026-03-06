namespace Emit.Abstractions;

using System.Diagnostics;
using Emit.Abstractions.Metrics;

/// <summary>
/// Default <see cref="IDistributedLock"/> implementation that delegates release and extend
/// operations to the <see cref="DistributedLockProviderBase"/> that created it.
/// </summary>
internal sealed class DistributedLock(
    DistributedLockProviderBase provider,
    string key,
    Guid lockId,
    LockMetrics? lockMetrics = null) : IDistributedLock
{
    private readonly long acquiredTicks = lockMetrics is not null ? Stopwatch.GetTimestamp() : 0;

    private int disposed;

    /// <inheritdoc />
    public string Key { get; } = key;

    /// <inheritdoc />
    public async Task<bool> ExtendAsync(TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) == 1, this);
        return await provider.ExtendLockAsync(Key, lockId, ttl, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            await provider.ReleaseLockAsync(Key, lockId, CancellationToken.None).ConfigureAwait(false);

            lockMetrics?.RecordHeldDuration(Stopwatch.GetElapsedTime(acquiredTicks).TotalSeconds);
        }
    }
}

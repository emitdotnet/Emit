namespace Emit.Abstractions;

/// <summary>
/// Repository interface for managing the global worker lease.
/// </summary>
/// <remarks>
/// <para>
/// The global lease ensures only one worker processes the outbox at a time (V1 concurrency model).
/// A single row/document in the database acts as the lease, with fields for <c>WorkerId</c>
/// and <c>LeaseUntil</c>.
/// </para>
/// <para>
/// <b>Acquisition Algorithm:</b>
/// <code>
/// UPDATE lease SET WorkerId = X, LeaseUntil = now + duration
/// WHERE LeaseUntil &lt; now OR WorkerId = X
/// </code>
/// If the update affects one row, Worker X holds the lease. If zero rows affected,
/// another worker holds it.
/// </para>
/// </remarks>
public interface ILeaseRepository
{
    /// <summary>
    /// Attempts to acquire or renew the global lease.
    /// </summary>
    /// <param name="workerId">The unique identifier of the worker attempting to acquire the lease.</param>
    /// <param name="leaseDuration">How long the lease should be held.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="LeaseAcquisitionResult"/> indicating whether the lease was acquired,
    /// and if not, when the current lease expires.
    /// </returns>
    /// <remarks>
    /// This operation is atomic. If successful, the worker holds the lease until
    /// <c>LeaseUntil</c>. The worker should call this method again before the lease
    /// expires to renew it.
    /// </remarks>
    Task<LeaseAcquisitionResult> TryAcquireOrRenewLeaseAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the global lease held by the specified worker.
    /// </summary>
    /// <param name="workerId">The unique identifier of the worker releasing the lease.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the lease was released; false if the worker didn't hold the lease.</returns>
    /// <remarks>
    /// This method sets <c>LeaseUntil</c> to the past, allowing another worker to
    /// immediately acquire the lease. This is called during graceful shutdown.
    /// </remarks>
    Task<bool> ReleaseLeaseAsync(string workerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the lease document/row exists in the database.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This method should be called during startup to ensure the lease storage exists.
    /// It's idempotent - calling it multiple times has no adverse effect.
    /// </remarks>
    Task EnsureLeaseExistsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a lease acquisition attempt.
/// </summary>
/// <param name="Acquired">Whether the lease was successfully acquired or renewed.</param>
/// <param name="LeaseUntil">
/// When the lease expires. If <paramref name="Acquired"/> is false, this indicates
/// when the current holder's lease expires (useful for smart sleep).
/// </param>
/// <param name="CurrentHolderId">
/// The worker ID of the current lease holder, if known. Null if acquired successfully
/// or if the holder cannot be determined.
/// </param>
public sealed record LeaseAcquisitionResult(
    bool Acquired,
    DateTime LeaseUntil,
    string? CurrentHolderId = null);

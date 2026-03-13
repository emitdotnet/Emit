namespace Emit.Abstractions;

using Emit.Models;

/// <summary>
/// Repository interface for outbox entry persistence operations.
/// </summary>
/// <remarks>
/// This interface defines the contract for persisting and querying outbox entries.
/// Implementations are provided by persistence providers.
/// </remarks>
public interface IOutboxRepository
{
    /// <summary>
    /// Enqueues a new entry to the outbox.
    /// </summary>
    /// <param name="entry">The outbox entry to enqueue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// The entry's <see cref="OutboxEntry.Sequence"/> is assigned by the repository during
    /// this operation. The mechanism is provider-specific (e.g., database auto-increment
    /// for PostgreSQL, atomic counter for MongoDB).
    /// </para>
    /// <para>
    /// Repositories that require transaction support access the transaction from
    /// <see cref="IEmitContext.Transaction"/> which is injected as a scoped dependency.
    /// </para>
    /// </remarks>
    Task EnqueueAsync(OutboxEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an outbox entry after successful processing.
    /// </summary>
    /// <param name="entryId">The unique identifier of the entry to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Called by the worker after an entry is successfully processed.
    /// The delete must be confirmed before processing the next entry to prevent
    /// duplicate delivery if the worker dies mid-flight.
    /// </remarks>
    Task DeleteAsync(object entryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the next batch of pending entries ordered by sequence.
    /// </summary>
    /// <param name="batchSize">Maximum number of entries to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of the oldest pending entries ordered by sequence ascending.</returns>
    /// <remarks>
    /// Entries are returned in global sequence order (FIFO). The caller is responsible
    /// for grouping entries by <see cref="OutboxEntry.GroupKey"/> and processing each
    /// group's entries strictly in sequence order.
    /// </remarks>
    Task<IReadOnlyList<OutboxEntry>> GetBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default);
}

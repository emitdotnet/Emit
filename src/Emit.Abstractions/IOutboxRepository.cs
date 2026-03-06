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
    /// Gets the head entry (first entry) for each distinct group.
    /// </summary>
    /// <param name="limit">Maximum number of group heads to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of entries, one per distinct group, ordered by sequence within each group.</returns>
    /// <remarks>
    /// This method is used by the worker to evaluate which groups have pending work.
    /// Each returned entry is the first entry in its group, ordered by sequence.
    /// </remarks>
    Task<IReadOnlyList<OutboxEntry>> GetGroupHeadsAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a batch of entries from the specified eligible groups.
    /// </summary>
    /// <param name="eligibleGroups">The group keys that are eligible for processing.</param>
    /// <param name="batchSize">Maximum number of entries to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of entries ordered by group key and sequence.</returns>
    /// <remarks>
    /// <para>
    /// This method fetches entries only from groups that have been determined eligible
    /// by evaluating their group heads.
    /// </para>
    /// <para>
    /// The entries are ordered by <c>GroupKey</c>, then by <c>Sequence</c> within each group.
    /// This allows the worker to partition by group and process each group's entries
    /// strictly in sequence order.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<OutboxEntry>> GetBatchAsync(
        IEnumerable<string> eligibleGroups,
        int batchSize,
        CancellationToken cancellationToken = default);
}

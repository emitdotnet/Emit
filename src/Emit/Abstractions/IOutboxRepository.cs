namespace Emit.Abstractions;

using Emit.Models;
using Transactional.Abstractions;

/// <summary>
/// Repository interface for outbox entry persistence operations.
/// </summary>
/// <remarks>
/// <para>
/// This interface defines the contract for persisting and querying outbox entries.
/// Implementations are provided by persistence providers (MongoDB, PostgreSQL).
/// </para>
/// <para>
/// <b>Transaction Semantics:</b>
/// <list type="bullet">
/// <item>
/// <description>
/// When <c>ITransactionContext</c> is provided, operations participate in that transaction.
/// The persistence provider extracts the appropriate context (e.g., IMongoTransactionContext,
/// IPostgresTransactionContext) to access the underlying session/transaction.
/// </description>
/// </item>
/// <item>
/// <description>
/// When <c>ITransactionContext</c> is null, operations execute immediately without
/// transactional guarantees. This is useful for best-effort scenarios where atomicity
/// with business data is not required.
/// </description>
/// </item>
/// </list>
/// </para>
/// <para>
/// <b>Future Enhancement:</b>
/// The current implementation enqueues entries synchronously during the transaction.
/// A future version will use <c>ITransactionContext.OnCommitted</c> (not yet available
/// in the Transactional library) to defer the actual enqueue until transaction commit,
/// ensuring true atomicity between business data and outbox entries.
/// </para>
/// </remarks>
public interface IOutboxRepository
{
    /// <summary>
    /// Enqueues a new entry to the outbox.
    /// </summary>
    /// <param name="entry">The outbox entry to enqueue.</param>
    /// <param name="transaction">
    /// Optional transaction context. When provided, the enqueue operation participates
    /// in the transaction. When null, the operation executes immediately.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// The entry's <see cref="OutboxEntry.Sequence"/> is assigned during this operation
    /// using <see cref="GetNextSequenceAsync"/> if not already set.
    /// </remarks>
    Task EnqueueAsync(OutboxEntry entry, ITransactionContext? transaction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the next sequence number for a group.
    /// </summary>
    /// <param name="groupKey">The group key to get the next sequence for.</param>
    /// <param name="transaction">
    /// Optional transaction context. The sequence generation should participate in
    /// the same transaction as the enqueue operation to maintain ordering guarantees.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The next sequence number for the group.</returns>
    /// <remarks>
    /// <para>
    /// The sequence number must be globally ordered within a group across all application replicas.
    /// </para>
    /// <para>
    /// <b>PostgreSQL:</b> Uses native <c>BIGINT GENERATED ALWAYS AS IDENTITY</c> or a database sequence.
    /// </para>
    /// <para>
    /// <b>MongoDB:</b> Uses an atomic counter in a dedicated collection with <c>FindOneAndUpdate</c>
    /// and <c>$inc</c> operator.
    /// </para>
    /// </remarks>
    Task<long> GetNextSequenceAsync(string groupKey, ITransactionContext? transaction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the status and related fields of an outbox entry.
    /// </summary>
    /// <param name="entryId">The unique identifier of the entry to update.</param>
    /// <param name="status">The new status.</param>
    /// <param name="completedAt">When the entry was completed (for <see cref="OutboxStatus.Completed"/>).</param>
    /// <param name="lastAttemptedAt">When the last attempt occurred.</param>
    /// <param name="retryCount">The updated retry count.</param>
    /// <param name="latestError">The latest error message (null to clear).</param>
    /// <param name="attempt">The attempt to add to the attempts list (null if none).</param>
    /// <param name="maxAttempts">Maximum number of attempts to retain in the attempts list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This method is called by the worker after processing an entry to update its status.
    /// The update must be confirmed before processing the next entry to prevent data loss
    /// if the worker dies mid-flight.
    /// </remarks>
    Task UpdateStatusAsync(
        object entryId,
        OutboxStatus status,
        DateTime? completedAt,
        DateTime? lastAttemptedAt,
        int? retryCount,
        string? latestError,
        OutboxAttempt? attempt,
        int maxAttempts = OutboxEntry.DefaultMaxAttempts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the head entry (first non-completed entry) for each distinct group.
    /// </summary>
    /// <param name="limit">Maximum number of group heads to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of entries, one per distinct group, ordered by sequence within each group.</returns>
    /// <remarks>
    /// <para>
    /// This method is used by the worker to evaluate which groups have pending work.
    /// Each returned entry is the first non-completed entry in its group, ordered by sequence.
    /// </para>
    /// <para>
    /// Group heads are evaluated to determine eligibility:
    /// <list type="bullet">
    /// <item><description>Pending entries are eligible for processing.</description></item>
    /// <item><description>Failed entries with elapsed backoff are eligible for retry.</description></item>
    /// <item><description>Failed entries with backoff not elapsed are skipped.</description></item>
    /// </list>
    /// </para>
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
    /// by evaluating their group heads. This respects ordering constraints - groups with
    /// failed entries at the head are excluded until they become eligible for retry.
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

    /// <summary>
    /// Deletes completed entries older than the specified retention period.
    /// </summary>
    /// <param name="completedBefore">Delete entries completed before this time.</param>
    /// <param name="batchSize">Maximum number of entries to delete in one operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of entries deleted.</returns>
    /// <remarks>
    /// This method is called by the cleanup background task to purge old completed entries.
    /// The cleanup runs at a configurable interval and deletes entries in batches to avoid
    /// overwhelming the database.
    /// </remarks>
    Task<int> DeleteCompletedEntriesAsync(
        DateTime completedBefore,
        int batchSize,
        CancellationToken cancellationToken = default);
}

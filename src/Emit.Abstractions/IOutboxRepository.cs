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
    /// Deletes delivered outbox entries in one database call.
    /// </summary>
    /// <param name="entryIds">The unique identifiers of the entries to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Called by the worker once a batch was delivered, before it reads the next batch. Entries that no longer
    /// exist are ignored. An entry left undeleted, for example because the call failed, is delivered again.
    /// </remarks>
    Task DeleteAsync(IReadOnlyCollection<object> entryIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the next batch of pending entries of a service, for the given providers, ordered by sequence.
    /// </summary>
    /// <param name="serviceName">The service whose entries are returned.</param>
    /// <param name="systemIds">The providers whose entries are returned, by <see cref="OutboxEntry.SystemId"/>.</param>
    /// <param name="batchSize">Maximum number of entries to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of the matching oldest pending entries ordered by sequence ascending.</returns>
    /// <remarks>
    /// Only entries whose <see cref="OutboxEntry.ServiceName"/> and <see cref="OutboxEntry.SystemId"/> match are
    /// returned, in sequence order (FIFO). Entries of other providers stay pending for a node that has them. The
    /// caller is responsible for grouping entries by <see cref="OutboxEntry.GroupKey"/> and processing each group's
    /// entries strictly in sequence order.
    /// </remarks>
    Task<IReadOnlyList<OutboxEntry>> GetBatchAsync(
        string serviceName,
        IReadOnlyCollection<string> systemIds,
        int batchSize,
        CancellationToken cancellationToken = default);
}

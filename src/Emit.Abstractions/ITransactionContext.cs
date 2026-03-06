namespace Emit.Abstractions;

/// <summary>
/// Represents a transaction context that can be committed or rolled back.
/// </summary>
public interface ITransactionContext : IAsyncDisposable
{
    /// <summary>
    /// Gets a value indicating whether the transaction has been committed.
    /// </summary>
    bool IsCommitted { get; }

    /// <summary>
    /// Gets a value indicating whether the transaction has been rolled back.
    /// </summary>
    bool IsRolledBack { get; }

    /// <summary>
    /// Asynchronously commits the transaction.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that represents the asynchronous commit operation.</returns>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously rolls back the transaction.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that represents the asynchronous rollback operation.</returns>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

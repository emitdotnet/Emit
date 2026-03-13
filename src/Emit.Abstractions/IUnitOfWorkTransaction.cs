namespace Emit.Abstractions;

/// <summary>
/// Represents an active transactional unit of work. Commit to persist all business data
/// and outbox entries atomically. Dispose without committing to roll back.
/// </summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    /// <summary>
    /// Whether <see cref="CommitAsync"/> has been called successfully.
    /// </summary>
    bool IsCommitted { get; }

    /// <summary>
    /// Whether <see cref="RollbackAsync"/> has been called.
    /// </summary>
    bool IsRolledBack { get; }

    /// <summary>
    /// Atomically commits all pending business data writes and outbox entries.
    /// For EF Core, this calls <c>SaveChangesAsync</c> followed by the database transaction commit.
    /// For MongoDB, this commits the client session transaction.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicitly rolls back all pending writes. Also occurs automatically on dispose
    /// if <see cref="CommitAsync"/> was not called.
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

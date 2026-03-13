namespace Emit.Abstractions;

/// <summary>
/// Manages the lifecycle of a transactional unit of work that coordinates business data writes
/// and outbox entry enqueue operations within a single atomic transaction.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Begins a new transactional unit of work. The returned transaction must be disposed
    /// after use. If <see cref="IUnitOfWorkTransaction.CommitAsync"/> is not called before
    /// disposal, the transaction is automatically rolled back.
    /// </summary>
    ValueTask<IUnitOfWorkTransaction> BeginAsync(CancellationToken cancellationToken = default);
}

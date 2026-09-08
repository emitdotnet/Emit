namespace Emit.MongoDB;

using Emit.Abstractions;
using global::MongoDB.Driver;

/// <summary>
/// MongoDB implementation of <see cref="IMongoUnitOfWorkTransaction"/>.
/// </summary>
internal sealed class MongoUnitOfWorkTransaction(
    IClientSessionHandle session,
    MongoTransactionContext transactionContext,
    MongoSessionHolder sessionHolder,
    IEmitContext emitContext) : IMongoUnitOfWorkTransaction
{
    /// <inheritdoc/>
    public IClientSessionHandle Session => session;

    /// <inheritdoc/>
    public bool IsCommitted => transactionContext.IsCommitted;

    /// <inheritdoc/>
    public bool IsRolledBack => transactionContext.IsRolledBack;

    /// <inheritdoc/>
    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        transactionContext.CommitAsync(cancellationToken);

    /// <inheritdoc/>
    public Task RollbackAsync(CancellationToken cancellationToken = default) =>
        transactionContext.RollbackAsync(cancellationToken);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await transactionContext.DisposeAsync().ConfigureAwait(false);
        sessionHolder.Session = null;

        // The ambient transaction is scoped, so leaving it behind would make the next
        // transaction in the same scope collide with this finished one. Cleared only when the
        // context still holds this transaction, so an unrelated one is never detached.
        if (ReferenceEquals(emitContext.Transaction, transactionContext))
        {
            emitContext.Transaction = null;
        }
    }
}

namespace Emit.MongoDB;

using global::MongoDB.Driver;

/// <summary>
/// MongoDB implementation of <see cref="IMongoUnitOfWorkTransaction"/>.
/// </summary>
internal sealed class MongoUnitOfWorkTransaction(
    IClientSessionHandle session,
    MongoTransactionContext transactionContext,
    MongoSessionHolder sessionHolder) : IMongoUnitOfWorkTransaction
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
    }
}

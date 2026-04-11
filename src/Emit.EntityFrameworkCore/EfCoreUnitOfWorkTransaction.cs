namespace Emit.EntityFrameworkCore;

using Emit.Abstractions;
using Microsoft.EntityFrameworkCore;

internal sealed class EfCoreUnitOfWorkTransaction(
    DbContext dbContext,
    EfCoreTransactionContext transactionContext) : IUnitOfWorkTransaction
{
    private enum TransactionState { Active, Committed, RolledBack }

    private TransactionState state = TransactionState.Active;

    /// <inheritdoc/>
    public bool IsCommitted => state == TransactionState.Committed;

    /// <inheritdoc/>
    public bool IsRolledBack => state == TransactionState.RolledBack;

    /// <inheritdoc/>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (IsCommitted)
        {
            throw new InvalidOperationException("Transaction has already been committed.");
        }
        if (IsRolledBack)
        {
            throw new InvalidOperationException("Transaction has already been rolled back.");
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transactionContext.CommitAsync(cancellationToken).ConfigureAwait(false);
        state = TransactionState.Committed;
    }

    /// <inheritdoc/>
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (IsCommitted)
        {
            throw new InvalidOperationException("Cannot rollback a committed transaction.");
        }

        state = TransactionState.RolledBack;
        await transactionContext.RollbackAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (!IsCommitted && !IsRolledBack)
        {
            try
            {
                await RollbackAsync().ConfigureAwait(false);
            }
            catch
            {
                // Swallow disposal errors
            }
        }

        await transactionContext.DisposeAsync().ConfigureAwait(false);
    }
}

namespace Emit.EntityFrameworkCore;

using Emit.Abstractions;
using Microsoft.EntityFrameworkCore;

internal sealed class EfCoreUnitOfWorkTransaction(
    DbContext dbContext,
    EfCoreTransactionContext transactionContext) : IUnitOfWorkTransaction
{
    /// <inheritdoc/>
    public bool IsCommitted { get; private set; }

    /// <inheritdoc/>
    public bool IsRolledBack { get; private set; }

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
        IsCommitted = true;
    }

    /// <inheritdoc/>
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (IsCommitted)
        {
            throw new InvalidOperationException("Cannot rollback a committed transaction.");
        }

        IsRolledBack = true;
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

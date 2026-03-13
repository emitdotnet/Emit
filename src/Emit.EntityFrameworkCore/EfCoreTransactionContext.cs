namespace Emit.EntityFrameworkCore;

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;

/// <summary>
/// EF Core implementation of transaction context.
/// </summary>
internal sealed class EfCoreTransactionContext : Abstractions.ITransactionContext
{
    private readonly IDbContextTransaction contextTransaction;

    /// <summary>
    /// Initializes a new instance of the <see cref="EfCoreTransactionContext"/> class.
    /// </summary>
    /// <param name="contextTransaction">The EF Core context transaction.</param>
    public EfCoreTransactionContext(IDbContextTransaction contextTransaction)
    {
        ArgumentNullException.ThrowIfNull(contextTransaction);
        this.contextTransaction = contextTransaction;
    }

    /// <inheritdoc/>
    public DbTransaction Transaction => contextTransaction.GetDbTransaction();

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

        await contextTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        IsCommitted = true;
    }

    /// <inheritdoc/>
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (IsCommitted)
        {
            throw new InvalidOperationException("Cannot rollback a committed transaction.");
        }
        if (IsRolledBack)
        {
            return; // Already rolled back
        }

        await contextTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        IsRolledBack = true;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // Auto-abort if not committed
        if (!IsCommitted && !IsRolledBack)
        {
            try
            {
                await contextTransaction.RollbackAsync().ConfigureAwait(false);
                IsRolledBack = true;
            }
            catch
            {
                // Swallow disposal errors
            }
        }

        await contextTransaction.DisposeAsync().ConfigureAwait(false);
    }
}

namespace Emit.MongoDB;

using global::MongoDB.Driver;

/// <summary>
/// MongoDB implementation of transaction context.
/// </summary>
internal sealed class MongoTransactionContext : Abstractions.ITransactionContext
{
    /// <inheritdoc/>
    public required IClientSessionHandle Session { get; init; }

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

        await Session.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
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

        await Session.AbortTransactionAsync(cancellationToken).ConfigureAwait(false);
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
                await Session.AbortTransactionAsync().ConfigureAwait(false);
                IsRolledBack = true;
            }
            catch
            {
                // Swallow disposal errors
            }
        }

        Session.Dispose();
    }
}

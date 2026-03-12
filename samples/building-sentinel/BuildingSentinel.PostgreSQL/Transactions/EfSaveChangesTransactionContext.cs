namespace BuildingSentinel.PostgreSQL.Transactions;

using Emit.Abstractions;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// A lightweight <see cref="ITransactionContext"/> for PostgreSQL that relies solely on
/// EF Core's atomic <c>SaveChangesAsync</c> rather than an explicit DB transaction.
/// Business entities and outbox entries are tracked together and flushed in a single call on commit,
/// which is sufficient for the transactional outbox guarantee without the overhead of a serializable transaction.
/// </summary>
internal sealed class EfSaveChangesTransactionContext(DbContext dbContext) : ITransactionContext
{
    public bool IsCommitted { get; private set; }

    public bool IsRolledBack { get; private set; }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (IsCommitted)
        {
            throw new InvalidOperationException("Transaction has already been committed.");
        }

        IsCommitted = true;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        IsRolledBack = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

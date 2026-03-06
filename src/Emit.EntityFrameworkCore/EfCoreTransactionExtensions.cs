namespace Emit.EntityFrameworkCore;

using Emit.Abstractions;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Extension methods for beginning EF Core transactions on <see cref="IEmitContext"/>.
/// </summary>
public static class EfCoreTransactionExtensions
{
    /// <summary>
    /// Begins an EF Core transaction and associates it with the emit context.
    /// </summary>
    /// <param name="context">The emit context.</param>
    /// <param name="dbContext">The EF Core DbContext (typically scoped).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The EF Core transaction context.</returns>
    /// <exception cref="ArgumentNullException">Thrown if context or dbContext is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if a transaction is already active.</exception>
    public static async Task<IEfCoreTransactionContext> BeginTransactionAsync(
        this IEmitContext context,
        DbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dbContext);

        if (context.Transaction is not null)
        {
            throw new InvalidOperationException(
                "A transaction is already active on this context. " +
                "Commit or rollback the existing transaction before starting a new one.");
        }

        var contextTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var efCoreTransaction = new EfCoreTransactionContext(contextTransaction);
        context.Transaction = efCoreTransaction;

        return efCoreTransaction;
    }
}

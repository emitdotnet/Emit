namespace Emit.EntityFrameworkCore;

using Emit.Abstractions;
using Microsoft.EntityFrameworkCore;

internal sealed class EfCoreUnitOfWork<TDbContext>(
    TDbContext dbContext,
    IEmitContext emitContext) : IUnitOfWork
    where TDbContext : DbContext
{
    /// <inheritdoc/>
    public async ValueTask<IUnitOfWorkTransaction> BeginAsync(CancellationToken cancellationToken = default)
    {
        var contextTransaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var transactionContext = new EfCoreTransactionContext(contextTransaction);
        emitContext.Transaction = transactionContext;

        return new EfCoreUnitOfWorkTransaction(dbContext, transactionContext);
    }
}

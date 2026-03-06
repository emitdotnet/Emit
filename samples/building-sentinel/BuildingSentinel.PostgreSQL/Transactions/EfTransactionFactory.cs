namespace BuildingSentinel.PostgreSQL.Transactions;

using BuildingSentinel.Common.Transactions;
using Emit.Abstractions;
using Emit.EntityFrameworkCore;

internal sealed class EfTransactionFactory(SampleDbContext dbContext) : ITransactionFactory
{
    public async Task<ITransactionContext> BeginTransactionAsync(
        IEmitContext emitContext,
        CancellationToken cancellationToken = default)
    {
        return await emitContext
            .BeginTransactionAsync(dbContext, cancellationToken)
            .ConfigureAwait(false);
    }
}

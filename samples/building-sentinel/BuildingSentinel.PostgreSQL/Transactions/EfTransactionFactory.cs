namespace BuildingSentinel.PostgreSQL.Transactions;

using BuildingSentinel.Common.Transactions;
using Emit.Abstractions;

internal sealed class EfTransactionFactory(SampleDbContext dbContext) : ITransactionFactory
{
    public Task<ITransactionContext> BeginTransactionAsync(
        IEmitContext emitContext,
        CancellationToken cancellationToken = default)
    {
        var transaction = new EfSaveChangesTransactionContext(dbContext);
        emitContext.Transaction = transaction;
        return Task.FromResult<ITransactionContext>(transaction);
    }
}

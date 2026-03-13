namespace Emit.MongoDB;

using Emit.Abstractions;
using Emit.MongoDB.Configuration;
using global::MongoDB.Driver;

/// <summary>
/// MongoDB implementation of <see cref="IUnitOfWork"/>.
/// </summary>
internal sealed class MongoUnitOfWork(
    MongoDbContext mongoContext,
    IEmitContext emitContext,
    MongoSessionHolder sessionHolder) : IUnitOfWork
{
    /// <inheritdoc/>
    public async ValueTask<IUnitOfWorkTransaction> BeginAsync(CancellationToken cancellationToken = default)
    {
        var session = await mongoContext.Client
            .StartSessionAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        session.StartTransaction();

        var transactionContext = new MongoTransactionContext { Session = session };

        emitContext.Transaction = transactionContext;
        sessionHolder.Session = session;

        return new MongoUnitOfWorkTransaction(session, transactionContext, sessionHolder);
    }
}

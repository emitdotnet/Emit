namespace BuildingSentinel.MongoDB.Transactions;

using BuildingSentinel.Common.Transactions;
using Emit.Abstractions;
using Emit.MongoDB;
using global::MongoDB.Driver;

internal sealed class MongoTransactionFactory(IMongoClient mongoClient) : ITransactionFactory
{
    public async Task<ITransactionContext> BeginTransactionAsync(
        IEmitContext emitContext,
        CancellationToken cancellationToken = default)
    {
        return await emitContext
            .BeginMongoTransactionAsync(mongoClient, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}

namespace Emit.MongoDB;

using Emit.Abstractions;
using global::MongoDB.Driver;

/// <summary>
/// Extension methods for beginning MongoDB transactions on <see cref="IEmitContext"/>.
/// </summary>
public static class MongoTransactionExtensions
{
    /// <summary>
    /// Begins a MongoDB transaction and associates it with the emit context.
    /// </summary>
    /// <param name="context">The emit context.</param>
    /// <param name="mongoClient">The MongoDB client (typically singleton).</param>
    /// <param name="options">Optional session options.</param>
    /// <param name="transactionOptions">Optional transaction options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The MongoDB transaction context.</returns>
    /// <exception cref="ArgumentNullException">Thrown if context or mongoClient is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if a transaction is already active.</exception>
    public static async Task<IMongoTransactionContext> BeginMongoTransactionAsync(
        this IEmitContext context,
        IMongoClient mongoClient,
        ClientSessionOptions? options = null,
        TransactionOptions? transactionOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mongoClient);

        if (context.Transaction is not null)
        {
            throw new InvalidOperationException(
                "A transaction is already active on this context. " +
                "Commit or rollback the existing transaction before starting a new one.");
        }

        var session = await mongoClient.StartSessionAsync(options, cancellationToken)
            .ConfigureAwait(false);

        session.StartTransaction(transactionOptions);

        var mongoTransaction = new MongoTransactionContext { Session = session };
        context.Transaction = mongoTransaction;

        return mongoTransaction;
    }
}

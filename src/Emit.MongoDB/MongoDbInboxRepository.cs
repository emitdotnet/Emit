namespace Emit.MongoDB;

using Emit.Abstractions;
using Emit.MongoDB.Configuration;
using Emit.MongoDB.Models;
using global::MongoDB.Driver;
using Microsoft.Extensions.Options;

/// <summary>
/// MongoDB implementation of <see cref="IInboxRepository"/>. A record is written with an upsert that only sets its
/// fields when it inserts, in the session of the current unit of work: a record already there is left as it is and
/// raises no error, which would abort the transaction. A concurrent upsert of the same record fails with a write
/// conflict, handled like any other transient failure of the consumer.
/// </summary>
internal sealed class MongoDbInboxRepository(
    MongoDbContext context,
    IEmitContext emitContext,
    IOptions<MongoDbInboxOptions> options,
    TimeProvider timeProvider) : IInboxRepository
{
    private static readonly UpdateOptions UpsertOptions = new() { IsUpsert = true };

    private IMongoCollection<InboxDocument> Collection => context.InboxCollection
        ?? throw new InvalidOperationException("The inbox collection is not configured. Enable the inbox on the MongoDB provider.");

    /// <inheritdoc/>
    public async Task<bool> TryRecordAsync(string handler, string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(messageId);
        var session = GetSession();

        var result = await Collection
            .UpdateOneAsync(session, Filter(handler, messageId), Update(), UpsertOptions, cancellationToken)
            .ConfigureAwait(false);

        return result.UpsertedId is not null;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlySet<string>> RecordAsync(
        string handler,
        IReadOnlyCollection<string> messageIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(messageIds);
        var session = GetSession();

        if (messageIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        // In order, so a message id appearing twice is inserted by its first upsert and matched by the second
        var ids = messageIds.ToList();
        var update = Update();
        var models = ids
            .Select(id => new UpdateOneModel<InboxDocument>(Filter(handler, id), update) { IsUpsert = true })
            .ToList();

        var result = await Collection
            .BulkWriteAsync(session, models, new BulkWriteOptions { IsOrdered = true }, cancellationToken)
            .ConfigureAwait(false);

        return result.Upserts.Select(upsert => ids[upsert.Index]).ToHashSet(StringComparer.Ordinal);
    }

    private UpdateDefinition<InboxDocument> Update()
    {
        var handledAt = timeProvider.GetUtcNow().UtcDateTime;
        return Builders<InboxDocument>.Update
            .SetOnInsert(x => x.HandledAt, handledAt)
            .SetOnInsert(x => x.ExpiresAt, handledAt + options.Value.Retention);
    }

    private IClientSessionHandle GetSession() => emitContext.Transaction switch
    {
        MongoTransactionContext mongoTransaction => mongoTransaction.Session,
        null => throw new InvalidOperationException(
            "No transaction is active. The inbox records a message in the transaction of the consumer's unit of work."),
        var transaction => throw new InvalidOperationException(
            $"Transaction context type mismatch. Expected a MongoDB-compatible transaction but received {transaction.GetType().Name}."),
    };

    private static FilterDefinition<InboxDocument> Filter(string handler, string messageId) =>
        Builders<InboxDocument>.Filter.Eq(x => x.MessageId, messageId)
        & Builders<InboxDocument>.Filter.Eq(x => x.Handler, handler);
}

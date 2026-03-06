namespace Emit.MongoDB;

using Emit.Abstractions;
using Emit.Models;
using Emit.MongoDB.Configuration;
using Emit.MongoDB.Models;
using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Microsoft.Extensions.Logging;

/// <summary>
/// MongoDB implementation of the outbox repository.
/// </summary>
/// <remarks>
/// <para>
/// This implementation is designed for sharded MongoDB deployments with GroupKey as the shard key.
/// All queries include GroupKey as the first filter criterion for optimal shard routing.
/// </para>
/// <para>
/// The sequence counter uses a non-transactional <c>FindOneAndUpdate</c> with <c>$inc</c> to
/// guarantee unique, monotonically increasing sequence numbers even under concurrent transactions.
/// The outbox insert itself participates in the caller's transaction.
/// </para>
/// </remarks>
internal sealed class MongoDbOutboxRepository : IOutboxRepository
{
    private readonly IMongoCollection<OutboxEntry> outboxCollection;
    private readonly IMongoCollection<SequenceCounter> counterCollection;
    private readonly IEmitContext emitContext;
    private readonly ILogger<MongoDbOutboxRepository> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MongoDbOutboxRepository"/> class.
    /// </summary>
    /// <param name="context">The MongoDB context containing pre-resolved collection references.</param>
    /// <param name="emitContext">The Emit context for accessing transaction state.</param>
    /// <param name="logger">The logger.</param>
    public MongoDbOutboxRepository(
        MongoDbContext context,
        IEmitContext emitContext,
        ILogger<MongoDbOutboxRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(emitContext);
        ArgumentNullException.ThrowIfNull(logger);

        this.emitContext = emitContext;
        this.logger = logger;

        outboxCollection = context.OutboxCollection!;
        counterCollection = context.SequenceCollection!;
    }

    /// <inheritdoc/>
    public async Task EnqueueAsync(
        OutboxEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var session = GetSession(emitContext.Transaction);

        // Sequence counter is NON-transactional to avoid snapshot isolation duplicates
        entry.Sequence = await GetNextSequenceAsync(entry.GroupKey, cancellationToken)
            .ConfigureAwait(false);

        await outboxCollection.InsertOneAsync(session, entry, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<long> GetNextSequenceAsync(
        string groupKey,
        CancellationToken cancellationToken)
    {
        var filter = Builders<SequenceCounter>.Filter.Eq(x => x.Id, groupKey);
        var update = Builders<SequenceCounter>.Update.Inc(x => x.Sequence, 1);
        var findOptions = new FindOneAndUpdateOptions<SequenceCounter>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };

        var result = await counterCollection.FindOneAndUpdateAsync(
            filter, update, findOptions, cancellationToken).ConfigureAwait(false);

        ArgumentNullException.ThrowIfNull(result);

        return result.Sequence;
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(object entryId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryId);

        var objectId = ConvertToObjectId(entryId);
        var filter = Builders<OutboxEntry>.Filter.Eq(x => x.Id, objectId);

        var result = await outboxCollection.DeleteOneAsync(filter, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.DeletedCount == 0)
        {
            logger.LogWarning("Outbox entry not found for deletion: id={Id}", entryId);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OutboxEntry>> GetGroupHeadsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        // Aggregation pipeline to find the minimum sequence per group
        // CRITICAL: GroupKey must be in the initial $match for shard routing
        BsonDocument[] pipeline =
        [
            // Group by GroupKey and find minimum sequence
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$groupKey" },
                { "minSequence", new BsonDocument("$min", "$sequence") }
            }),
            // Limit to N groups
            new BsonDocument("$limit", limit)
        ];

        var groupResults = await outboxCollection
            .Aggregate<BsonDocument>(pipeline, cancellationToken: cancellationToken)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (groupResults.Count == 0)
        {
            return [];
        }

        // Build filter to fetch the actual entries
        // CRITICAL: GroupKey must be first for shard routing
        var filters = groupResults.Select(doc =>
        {
            var groupKey = doc["_id"].AsString;
            var minSequence = doc["minSequence"].ToInt64();
            return Builders<OutboxEntry>.Filter.And(
                Builders<OutboxEntry>.Filter.Eq(x => x.GroupKey, groupKey),
                Builders<OutboxEntry>.Filter.Eq(x => x.Sequence, minSequence));
        }).ToList();

        var combinedFilter = Builders<OutboxEntry>.Filter.Or(filters);

        var entries = await outboxCollection
            .Find(combinedFilter)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entries;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OutboxEntry>> GetBatchAsync(
        IEnumerable<string> eligibleGroups,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eligibleGroups);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var groupsList = eligibleGroups.ToList();
        if (groupsList.Count == 0)
        {
            return [];
        }

        // CRITICAL: GroupKey filter must be first for shard routing
        var filter = Builders<OutboxEntry>.Filter.In(x => x.GroupKey, groupsList);

        var sort = Builders<OutboxEntry>.Sort
            .Ascending(x => x.GroupKey)
            .Ascending(x => x.Sequence);

        var entries = await outboxCollection
            .Find(filter)
            .Sort(sort)
            .Limit(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entries;
    }

    private static IClientSessionHandle GetSession(ITransactionContext? transaction)
    {
        if (transaction is null)
        {
            throw new InvalidOperationException(
                "No transaction context is available. " +
                "A transactional outbox requires an active transaction.");
        }

        if (transaction is IMongoTransactionContext mongoTransaction)
        {
            return mongoTransaction.Session;
        }

        throw new InvalidOperationException(
            $"Transaction context type mismatch. Expected {nameof(IMongoTransactionContext)} " +
            $"but received {transaction.GetType().Name}. Ensure you are using MongoDB-compatible " +
            "transactions created via BeginMongoTransactionAsync().");
    }

    private static ObjectId ConvertToObjectId(object entryId)
    {
        return entryId switch
        {
            ObjectId objectId => objectId,
            string stringId => ObjectId.Parse(stringId),
            _ => throw new ArgumentException(
                $"Cannot convert {entryId.GetType().Name} to ObjectId. " +
                "Entry ID must be an ObjectId or a valid ObjectId string.",
                nameof(entryId))
        };
    }
}

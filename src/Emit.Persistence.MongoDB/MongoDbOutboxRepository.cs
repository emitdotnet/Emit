namespace Emit.Persistence.MongoDB;

using Emit.Abstractions;
using Emit.Models;
using Emit.Persistence.MongoDB.Configuration;
using Emit.Persistence.MongoDB.Models;
using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Transactional.Abstractions;
using Transactional.MongoDB;

/// <summary>
/// MongoDB implementation of the outbox repository.
/// </summary>
/// <remarks>
/// <para>
/// This implementation is designed for sharded MongoDB deployments with GroupKey as the shard key.
/// All queries include GroupKey as the first filter criterion for optimal shard routing.
/// </para>
/// <para>
/// Indexes created:
/// <list type="bullet">
/// <item><description>Unique compound index on {GroupKey: 1, Sequence: 1}</description></item>
/// <item><description>Compound index on {Status: 1, GroupKey: 1} for group head queries</description></item>
/// <item><description>Index on {CompletedAt: 1} for cleanup queries</description></item>
/// </list>
/// </para>
/// </remarks>
internal sealed class MongoDbOutboxRepository : IOutboxRepository, ILeaseRepository
{
    private readonly IMongoCollection<OutboxEntry> outboxCollection;
    private readonly IMongoCollection<SequenceCounter> counterCollection;
    private readonly IMongoCollection<LeaseDocument> leaseCollection;
    private readonly MongoDbOptions options;
    private readonly ILogger<MongoDbOutboxRepository> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MongoDbOutboxRepository"/> class.
    /// </summary>
    /// <param name="mongoClient">The MongoDB client.</param>
    /// <param name="options">The MongoDB options.</param>
    /// <param name="logger">The logger.</param>
    public MongoDbOutboxRepository(
        IMongoClient mongoClient,
        IOptions<MongoDbOptions> options,
        ILogger<MongoDbOutboxRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(mongoClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.options = options.Value;
        this.logger = logger;

        // Configure BSON serialization
        BsonConfiguration.Configure();

        // Get database and collections
        var database = mongoClient.GetDatabase(this.options.DatabaseName);
        outboxCollection = database.GetCollection<OutboxEntry>(this.options.CollectionName);
        counterCollection = database.GetCollection<SequenceCounter>(this.options.CounterCollectionName);
        leaseCollection = database.GetCollection<LeaseDocument>(this.options.LeaseCollectionName);

        logger.LogDebug(
            "MongoDbOutboxRepository initialized with database={Database}, collection={Collection}",
            this.options.DatabaseName,
            this.options.CollectionName);
    }

    /// <summary>
    /// Ensures all required indexes exist on the collections.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Creating indexes for outbox collection");

        // Unique compound index on {GroupKey, Sequence}
        // This is also the recommended sharding key pattern
        var groupKeySequenceIndex = new CreateIndexModel<OutboxEntry>(
            Builders<OutboxEntry>.IndexKeys
                .Ascending(x => x.GroupKey)
                .Ascending(x => x.Sequence),
            new CreateIndexOptions { Unique = true, Name = "groupKey_sequence_unique" });

        // Compound index on {Status, GroupKey} for GetGroupHeadsAsync queries
        var statusGroupKeyIndex = new CreateIndexModel<OutboxEntry>(
            Builders<OutboxEntry>.IndexKeys
                .Ascending(x => x.Status)
                .Ascending(x => x.GroupKey),
            new CreateIndexOptions { Name = "status_groupKey" });

        // Index on {CompletedAt} for cleanup queries
        var completedAtIndex = new CreateIndexModel<OutboxEntry>(
            Builders<OutboxEntry>.IndexKeys
                .Ascending(x => x.CompletedAt),
            new CreateIndexOptions { Name = "completedAt" });

        await outboxCollection.Indexes.CreateManyAsync(
            [groupKeySequenceIndex, statusGroupKeyIndex, completedAtIndex],
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Outbox collection indexes created successfully");
    }

    /// <inheritdoc/>
    public async Task EnqueueAsync(
        OutboxEntry entry,
        ITransactionContext? transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var session = GetSession(transaction);

        logger.LogDebug(
            "Enqueuing outbox entry: groupKey={GroupKey}, sequence={Sequence}, providerId={ProviderId}",
            entry.GroupKey, entry.Sequence, entry.ProviderId);

        if (session is not null)
        {
            await outboxCollection.InsertOneAsync(session, entry, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await outboxCollection.InsertOneAsync(entry, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        logger.LogInformation(
            "Outbox entry enqueued: id={Id}, groupKey={GroupKey}, sequence={Sequence}",
            entry.Id, entry.GroupKey, entry.Sequence);
    }

    /// <inheritdoc/>
    public async Task<long> GetNextSequenceAsync(
        string groupKey,
        ITransactionContext? transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupKey);

        var session = GetSession(transaction);

        // Atomic increment with upsert
        var filter = Builders<SequenceCounter>.Filter.Eq(x => x.Id, groupKey);
        var update = Builders<SequenceCounter>.Update.Inc(x => x.Sequence, 1);
        var findOptions = new FindOneAndUpdateOptions<SequenceCounter>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };

        logger.LogDebug("Getting next sequence for groupKey={GroupKey}", groupKey);

        SequenceCounter result;
        if (session is not null)
        {
            result = await counterCollection.FindOneAndUpdateAsync(
                session, filter, update, findOptions, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            result = await counterCollection.FindOneAndUpdateAsync(
                filter, update, findOptions, cancellationToken).ConfigureAwait(false);
        }

        logger.LogDebug("Sequence for groupKey={GroupKey} is now {Sequence}", groupKey, result.Sequence);

        return result.Sequence;
    }

    /// <inheritdoc/>
    public async Task UpdateStatusAsync(
        object entryId,
        OutboxStatus status,
        DateTime? completedAt,
        DateTime? lastAttemptedAt,
        int? retryCount,
        string? latestError,
        OutboxAttempt? attempt,
        int maxAttempts = OutboxEntry.DefaultMaxAttempts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryId);

        var objectId = ConvertToObjectId(entryId);
        var filter = Builders<OutboxEntry>.Filter.Eq(x => x.Id, objectId);

        var updateDefinitions = new List<UpdateDefinition<OutboxEntry>>
        {
            Builders<OutboxEntry>.Update.Set(x => x.Status, status)
        };

        if (completedAt.HasValue)
        {
            updateDefinitions.Add(Builders<OutboxEntry>.Update.Set(x => x.CompletedAt, completedAt));
        }

        if (retryCount.HasValue)
        {
            updateDefinitions.Add(Builders<OutboxEntry>.Update.Set(x => x.RetryCount, retryCount));
        }

        if (lastAttemptedAt.HasValue)
        {
            updateDefinitions.Add(Builders<OutboxEntry>.Update.Set(x => x.LastAttemptedAt, lastAttemptedAt));
        }

        // Set LatestError (null on success to clear previous error)
        updateDefinitions.Add(Builders<OutboxEntry>.Update.Set(x => x.LatestError, latestError));

        // If there's an attempt record, push it to the array with $slice to cap size
        if (attempt is not null)
        {
            updateDefinitions.Add(
                Builders<OutboxEntry>.Update.PushEach(
                    "attempts",
                    new[] { attempt },
                    slice: -maxAttempts));
        }

        var update = Builders<OutboxEntry>.Update.Combine(updateDefinitions);

        logger.LogDebug(
            "Updating outbox entry status: id={Id}, newStatus={Status}",
            entryId, status);

        var result = await outboxCollection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.ModifiedCount == 0)
        {
            logger.LogWarning("Outbox entry not found for status update: id={Id}", entryId);
        }
        else
        {
            logger.LogDebug("Outbox entry status updated: id={Id}, status={Status}", entryId, status);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OutboxEntry>> GetGroupHeadsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        logger.LogDebug("Getting group heads with limit={Limit}", limit);

        // Aggregation pipeline to find the minimum sequence per group for non-completed entries
        // CRITICAL: GroupKey must be in the initial $match for shard routing
        var pipeline = new[]
        {
            // Match non-completed entries
            new BsonDocument("$match", new BsonDocument
            {
                { "status", new BsonDocument("$ne", OutboxStatus.Completed.ToString()) }
            }),
            // Group by GroupKey and find minimum sequence
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$groupKey" },
                { "minSequence", new BsonDocument("$min", "$sequence") }
            }),
            // Limit to N groups
            new BsonDocument("$limit", limit)
        };

        var groupResults = await outboxCollection
            .Aggregate<BsonDocument>(pipeline, cancellationToken: cancellationToken)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (groupResults.Count == 0)
        {
            logger.LogDebug("No group heads found");
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

        logger.LogDebug("Retrieved {Count} group heads", entries.Count);

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
            logger.LogDebug("No eligible groups provided, returning empty batch");
            return [];
        }

        logger.LogDebug(
            "Getting batch with {EligibleGroupsCount} eligible groups, batchSize={BatchSize}",
            groupsList.Count, batchSize);

        // CRITICAL: GroupKey filter must be first for shard routing
        var filter = Builders<OutboxEntry>.Filter.And(
            Builders<OutboxEntry>.Filter.In(x => x.GroupKey, groupsList),
            Builders<OutboxEntry>.Filter.Ne(x => x.Status, OutboxStatus.Completed));

        var sort = Builders<OutboxEntry>.Sort
            .Ascending(x => x.GroupKey)
            .Ascending(x => x.Sequence);

        var entries = await outboxCollection
            .Find(filter)
            .Sort(sort)
            .Limit(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        logger.LogDebug("Retrieved batch of {Count} entries", entries.Count);

        return entries;
    }

    /// <inheritdoc/>
    public async Task<int> DeleteCompletedEntriesAsync(
        DateTime completedBefore,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        logger.LogDebug(
            "Deleting completed entries before {CompletedBefore} with batchSize={BatchSize}",
            completedBefore, batchSize);

        var filter = Builders<OutboxEntry>.Filter.And(
            Builders<OutboxEntry>.Filter.Eq(x => x.Status, OutboxStatus.Completed),
            Builders<OutboxEntry>.Filter.Lt(x => x.CompletedAt, completedBefore));

        // Find entries to delete (limited batch)
        var entriesToDelete = await outboxCollection
            .Find(filter)
            .Limit(batchSize)
            .Project(x => x.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entriesToDelete.Count == 0)
        {
            logger.LogDebug("No completed entries found for deletion");
            return 0;
        }

        var deleteFilter = Builders<OutboxEntry>.Filter.In(x => x.Id, entriesToDelete);
        var result = await outboxCollection.DeleteManyAsync(deleteFilter, cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation("Deleted {Count} completed outbox entries", result.DeletedCount);

        return (int)result.DeletedCount;
    }

    #region ILeaseRepository Implementation

    /// <inheritdoc/>
    public async Task<LeaseAcquisitionResult> TryAcquireOrRenewLeaseAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be positive.");
        }

        var now = DateTime.UtcNow;
        var leaseUntil = now.Add(leaseDuration);

        // Filter: lease expired OR already owned by this worker
        var filter = Builders<LeaseDocument>.Filter.And(
            Builders<LeaseDocument>.Filter.Eq(x => x.Id, LeaseDocument.GlobalLeaseId),
            Builders<LeaseDocument>.Filter.Or(
                Builders<LeaseDocument>.Filter.Lt(x => x.LeaseUntil, now),
                Builders<LeaseDocument>.Filter.Eq(x => x.WorkerId, workerId)));

        var update = Builders<LeaseDocument>.Update
            .Set(x => x.WorkerId, workerId)
            .Set(x => x.LeaseUntil, leaseUntil);

        var findOptions = new FindOneAndUpdateOptions<LeaseDocument>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };

        logger.LogDebug("Worker {WorkerId} attempting to acquire/renew lease", workerId);

        try
        {
            var result = await leaseCollection.FindOneAndUpdateAsync(
                filter, update, findOptions, cancellationToken).ConfigureAwait(false);

            if (result is not null && result.WorkerId == workerId)
            {
                logger.LogInformation(
                    "Worker {WorkerId} acquired/renewed lease until {LeaseUntil}",
                    workerId, leaseUntil);
                return new LeaseAcquisitionResult(Acquired: true, LeaseUntil: leaseUntil);
            }
        }
        catch (MongoCommandException ex) when (ex.CodeName == "DuplicateKey")
        {
            // Another worker acquired the lease first (race condition with upsert)
            logger.LogDebug("Worker {WorkerId} lost lease acquisition race", workerId);
        }

        // Lease is held by another worker
        var currentLease = await leaseCollection
            .Find(Builders<LeaseDocument>.Filter.Eq(x => x.Id, LeaseDocument.GlobalLeaseId))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (currentLease is not null)
        {
            logger.LogDebug(
                "Lease held by {OtherWorkerId} until {LeaseUntil}",
                currentLease.WorkerId, currentLease.LeaseUntil);
            return new LeaseAcquisitionResult(
                Acquired: false,
                LeaseUntil: currentLease.LeaseUntil,
                CurrentHolderId: currentLease.WorkerId);
        }

        return new LeaseAcquisitionResult(Acquired: false, LeaseUntil: DateTime.MinValue);
    }

    /// <inheritdoc/>
    public async Task<bool> ReleaseLeaseAsync(
        string workerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        var filter = Builders<LeaseDocument>.Filter.And(
            Builders<LeaseDocument>.Filter.Eq(x => x.Id, LeaseDocument.GlobalLeaseId),
            Builders<LeaseDocument>.Filter.Eq(x => x.WorkerId, workerId));

        // Set LeaseUntil to now to allow immediate re-acquisition
        var update = Builders<LeaseDocument>.Update.Set(x => x.LeaseUntil, DateTime.UtcNow);

        logger.LogDebug("Worker {WorkerId} releasing lease", workerId);

        var result = await leaseCollection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.ModifiedCount > 0)
        {
            logger.LogInformation("Worker {WorkerId} released lease", workerId);
            return true;
        }

        logger.LogDebug("Worker {WorkerId} had no lease to release", workerId);
        return false;
    }

    /// <inheritdoc/>
    public async Task EnsureLeaseExistsAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Ensuring lease document exists");

        var filter = Builders<LeaseDocument>.Filter.Eq(x => x.Id, LeaseDocument.GlobalLeaseId);
        var update = Builders<LeaseDocument>.Update
            .SetOnInsert(x => x.Id, LeaseDocument.GlobalLeaseId)
            .SetOnInsert(x => x.WorkerId, null)
            .SetOnInsert(x => x.LeaseUntil, DateTime.MinValue);

        var updateOptions = new UpdateOptions { IsUpsert = true };

        await leaseCollection.UpdateOneAsync(filter, update, updateOptions, cancellationToken)
            .ConfigureAwait(false);

        logger.LogDebug("Lease document ensured");
    }

    #endregion

    private static IClientSessionHandle? GetSession(ITransactionContext? transaction)
    {
        if (transaction is IMongoTransactionContext mongoTransaction)
        {
            return mongoTransaction.Session;
        }

        return null;
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

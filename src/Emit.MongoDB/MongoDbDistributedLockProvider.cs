namespace Emit.MongoDB;

using Emit.Abstractions;
using Emit.Abstractions.Metrics;
using Emit.MongoDB.Configuration;
using Emit.MongoDB.Models;
using global::MongoDB.Driver;
using Microsoft.Extensions.Logging;

/// <summary>
/// MongoDB implementation of <see cref="DistributedLockProviderBase"/>.
/// </summary>
internal sealed class MongoDbDistributedLockProvider : DistributedLockProviderBase
{
    private readonly IMongoCollection<LockDocument> lockCollection;
    private readonly ILogger<MongoDbDistributedLockProvider> logger;

    public MongoDbDistributedLockProvider(
        MongoDbContext context,
        IRandomProvider randomProvider,
        LockMetrics lockMetrics,
        ILogger<MongoDbDistributedLockProvider> logger) : base(randomProvider, lockMetrics: lockMetrics)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;

        lockCollection = context.LockCollection!;
    }

    /// <inheritdoc />
    protected override async Task<bool> TryAcquireCoreAsync(
        string key,
        Guid lockId,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        var ttlMs = (long)ttl.TotalMilliseconds;

        // Filter on key only — expiry logic lives entirely in the pipeline so that both
        // the "should I acquire?" decision and the new expiresAt value use the same $$NOW.
        // $expr is not permitted in upsert filter predicates (MongoDB restriction), which
        // is why the condition is expressed as a $cond inside the update pipeline instead.
        //
        // Pipeline logic:
        //   - If expiresAt is null (new document from upsert) or <= $$NOW (expired):
        //       set lockId to ours and expiresAt to $$NOW + ttl  (acquire)
        //   - Otherwise (active lock held by another):
        //       preserve existing lockId and expiresAt             (do not acquire)
        //
        // null < any date in MongoDB's aggregation comparison, so a freshly upserted
        // document (expiresAt: null) always satisfies the $lte condition.
        var filter = Builders<LockDocument>.Filter.Eq(x => x.Key, key);

        var pipeline = new EmptyPipelineDefinition<LockDocument>()
            .AppendStage<LockDocument, LockDocument, LockDocument>(
                $$"""
                { $set: {
                    lockId:    { $cond: [{ $lte: ['$expiresAt', '$$NOW'] }, UUID('{{lockId}}'),       '$lockId'] },
                    expiresAt: { $cond: [{ $lte: ['$expiresAt', '$$NOW'] }, { $add: ['$$NOW', {{ttlMs}}] }, '$expiresAt'] }
                } }
                """);

        var options = new FindOneAndUpdateOptions<LockDocument>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };

        try
        {
            var result = await lockCollection.FindOneAndUpdateAsync(
                filter,
                pipeline,
                options,
                cancellationToken).ConfigureAwait(false);

            return result is not null && result.LockId == lockId;
        }
        catch (MongoCommandException ex) when (ex.Code == 11000)
        {
            // Concurrent upsert race on the same key; another holder just acquired it.
            return false;
        }
    }

    /// <inheritdoc />
    protected override async Task ReleaseCoreAsync(
        string key,
        Guid lockId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<LockDocument>.Filter.And(
            Builders<LockDocument>.Filter.Eq(x => x.Key, key),
            Builders<LockDocument>.Filter.Eq(x => x.LockId, lockId));

        try
        {
            await lockCollection.DeleteOneAsync(filter, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to release lock for key '{Key}'. The lock will expire after its TTL.", key);
        }
    }

    /// <inheritdoc />
    protected override async Task<bool> ExtendCoreAsync(
        string key,
        Guid lockId,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        var ttlMs = (long)ttl.TotalMilliseconds;

        var filter = Builders<LockDocument>.Filter.And(
            Builders<LockDocument>.Filter.Eq(x => x.Key, key),
            Builders<LockDocument>.Filter.Eq(x => x.LockId, lockId));

        // Pipeline update uses $$NOW for server-side timestamps
        var pipeline = new EmptyPipelineDefinition<LockDocument>()
            .AppendStage<LockDocument, LockDocument, LockDocument>(
                $"{{ $set: {{ expiresAt: {{ $add: ['$$NOW', {ttlMs}] }} }} }}");

        var result = await lockCollection.FindOneAndUpdateAsync(
            filter,
            pipeline,
            new FindOneAndUpdateOptions<LockDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken).ConfigureAwait(false);

        return result is not null;
    }
}

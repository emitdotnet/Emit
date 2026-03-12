namespace Emit.MongoDB;

using Emit.Abstractions.LeaderElection;
using Emit.MongoDB.Configuration;
using Emit.MongoDB.Models;
using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Microsoft.Extensions.Logging;

/// <summary>
/// MongoDB implementation of <see cref="ILeaderElectionPersistence"/>.
/// </summary>
internal sealed class MongoDbLeaderElectionPersistence : ILeaderElectionPersistence
{
    private readonly IMongoCollection<LeaderDocument> leaderCollection;
    private readonly IMongoCollection<NodeDocument> nodeCollection;
    private readonly ILogger<MongoDbLeaderElectionPersistence> logger;

    public MongoDbLeaderElectionPersistence(
        MongoDbContext context,
        ILogger<MongoDbLeaderElectionPersistence> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;

        leaderCollection = context.LeaderCollection;
        nodeCollection = context.NodeCollection;
    }

    /// <inheritdoc />
    public async Task<HeartbeatResult> HeartbeatAsync(
        HeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        var ttlMs = (long)request.LeaseDuration.TotalMilliseconds;

        // 1. Upsert node registration with server-side timestamp
        var nodeFilter = Builders<NodeDocument>.Filter.Eq(x => x.NodeId, request.NodeId);
        var nodePipeline = new EmptyPipelineDefinition<NodeDocument>()
            .AppendStage<NodeDocument, NodeDocument, NodeDocument>(
                $"{{ $set: {{ instanceId: '{request.InstanceId}', startedAt: {{ $ifNull: ['$startedAt', '$$NOW'] }}, lastSeenAt: '$$NOW' }} }}");

        await nodeCollection.FindOneAndUpdateAsync(
            nodeFilter,
            nodePipeline,
            new FindOneAndUpdateOptions<NodeDocument> { IsUpsert = true },
            cancellationToken).ConfigureAwait(false);

        // 2. Atomic CAS: acquire or renew leader lease.
        // Filter on key only — the "who wins?" logic lives in the pipeline so that both
        // the decision and the new expiresAt reference the same $$NOW (server clock).
        // $expr is not permitted in upsert filter predicates (MongoDB restriction).
        //
        // Pipeline logic:
        //   - If expiresAt is null (new document) or <= $$NOW (expired), OR nodeId already ours:
        //       set nodeId to ours and expiresAt to $$NOW + lease  (acquire / renew)
        //   - Otherwise (active lease held by another node):
        //       preserve existing nodeId and expiresAt              (do not acquire)
        //
        // We then inspect result.NodeId to determine whether we hold leadership.
        var leaderFilter = Builders<LeaderDocument>.Filter.Eq(x => x.Key, "leader");

        var leaderPipeline = new EmptyPipelineDefinition<LeaderDocument>()
            .AppendStage<LeaderDocument, LeaderDocument, LeaderDocument>(
                $$"""
                { $set: {
                    nodeId: { $cond: [
                        { $or: [{ $eq: ['$nodeId', UUID('{{request.NodeId}}')] }, { $lte: ['$expiresAt', '$$NOW'] }] },
                        UUID('{{request.NodeId}}'),
                        '$nodeId'
                    ]},
                    expiresAt: { $cond: [
                        { $or: [{ $eq: ['$nodeId', UUID('{{request.NodeId}}')] }, { $lte: ['$expiresAt', '$$NOW'] }] },
                        { $add: ['$$NOW', {{ttlMs}}] },
                        '$expiresAt'
                    ]}
                } }
                """);

        try
        {
            var result = await leaderCollection.FindOneAndUpdateAsync(
                leaderFilter,
                leaderPipeline,
                new FindOneAndUpdateOptions<LeaderDocument>
                {
                    IsUpsert = true,
                    ReturnDocument = ReturnDocument.After
                },
                cancellationToken).ConfigureAwait(false);

            return new HeartbeatResult(result!.NodeId == request.NodeId, result.NodeId);
        }
        catch (MongoCommandException ex) when (ex.Code == 11000)
        {
            // Concurrent upsert race creating the first leader document.
            var currentLeader = await leaderCollection.Find(
                Builders<LeaderDocument>.Filter.Eq(x => x.Key, "leader"))
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            return new HeartbeatResult(false, currentLeader?.NodeId ?? Guid.Empty);
        }
    }

    /// <inheritdoc />
    public async Task ResignLeadershipAsync(
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<LeaderDocument>.Filter.And(
            Builders<LeaderDocument>.Filter.Eq(x => x.Key, "leader"),
            Builders<LeaderDocument>.Filter.Eq(x => x.NodeId, nodeId));

        try
        {
            await leaderCollection.DeleteOneAsync(filter, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to resign leadership for node {NodeId}", nodeId);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> RemoveExpiredNodesAsync(
        TimeSpan nodeRegistrationTtl,
        CancellationToken cancellationToken)
    {
        var cutoffMs = (long)nodeRegistrationTtl.TotalMilliseconds;

        // $$NOW used in both find and delete so both operations reference the same server clock
        var expiredFilter = new BsonDocumentFilterDefinition<NodeDocument>(
            new BsonDocument("$expr",
                new BsonDocument("$lt", new BsonArray
                {
                    "$lastSeenAt",
                    new BsonDocument("$subtract", new BsonArray { "$$NOW", cutoffMs })
                })));

        var expiredNodeIds = await nodeCollection
            .Find(expiredFilter)
            .Project(x => x.NodeId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (expiredNodeIds.Count == 0)
        {
            return [];
        }

        await nodeCollection.DeleteManyAsync(expiredFilter, cancellationToken).ConfigureAwait(false);

        return expiredNodeIds;
    }

    /// <inheritdoc />
    public async Task DeregisterNodeAsync(
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<NodeDocument>.Filter.Eq(x => x.NodeId, nodeId);

        try
        {
            await nodeCollection.DeleteOneAsync(filter, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to deregister node {NodeId}", nodeId);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetActiveNodeIdsAsync(
        CancellationToken cancellationToken)
    {
        return await nodeCollection
            .Find(Builders<NodeDocument>.Filter.Empty)
            .Project(x => x.NodeId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

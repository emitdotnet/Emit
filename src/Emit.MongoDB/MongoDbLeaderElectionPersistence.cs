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

        // 2. Atomic CAS: acquire or renew leader lease
        // Match if: this node is already leader OR the lease has expired
        var leaderFilter = Builders<LeaderDocument>.Filter.And(
            Builders<LeaderDocument>.Filter.Eq(x => x.Key, "leader"),
            Builders<LeaderDocument>.Filter.Or(
                Builders<LeaderDocument>.Filter.Eq(x => x.NodeId, request.NodeId),
                Builders<LeaderDocument>.Filter.Where(x => x.ExpiresAt <= DateTime.UtcNow)));

        var leaderPipeline = new EmptyPipelineDefinition<LeaderDocument>()
            .AppendStage<LeaderDocument, LeaderDocument, LeaderDocument>(
                $"{{ $set: {{ nodeId: UUID('{request.NodeId}'), expiresAt: {{ $add: ['$$NOW', {ttlMs}] }} }} }}");

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

            if (result is not null && result.NodeId == request.NodeId)
            {
                return new HeartbeatResult(true, request.NodeId);
            }

            // We got a document back but the nodeId doesn't match — shouldn't happen with upsert
            var leaderNodeId = result?.NodeId ?? Guid.Empty;
            return new HeartbeatResult(false, leaderNodeId);
        }
        catch (MongoCommandException ex) when (ex.Code == 11000)
        {
            // Duplicate key — another node holds the leader document and lease hasn't expired
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

        // Find expired nodes first so we can return their IDs
        var cutoffPipeline = new EmptyPipelineDefinition<NodeDocument>()
            .AppendStage<NodeDocument, NodeDocument, BsonDocument>(
                $"{{ $match: {{ $expr: {{ $lt: ['$lastSeenAt', {{ $subtract: ['$$NOW', {cutoffMs}] }}] }} }} }}");

        var expiredNodes = await nodeCollection.Aggregate(cutoffPipeline)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (expiredNodes is [])
        {
            return [];
        }

        var expiredNodeIds = expiredNodes
            .Select(d => d["_id"].AsGuid)
            .ToList();

        var deleteFilter = Builders<NodeDocument>.Filter.In(x => x.NodeId, expiredNodeIds);
        await nodeCollection.DeleteManyAsync(deleteFilter, cancellationToken).ConfigureAwait(false);

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
        var projection = Builders<NodeDocument>.Projection.Include(x => x.NodeId);
        var documents = await nodeCollection
            .Find(Builders<NodeDocument>.Filter.Empty)
            .Project<NodeDocument>(projection)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return documents.Select(d => d.NodeId).ToList();
    }
}

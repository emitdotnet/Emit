namespace Emit.MongoDB;

using Emit.Abstractions.LeaderElection;
using Emit.MongoDB.Configuration;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Models;
using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Microsoft.Extensions.Logging;

/// <summary>
/// MongoDB implementation of <see cref="ILeaderElectionPersistence"/>.
/// </summary>
/// <remarks>
/// Every service has its own lease document and its own node registrations. Updates are built as
/// <see cref="BsonDocument"/> pipelines and every configured value is passed as a <c>$literal</c>,
/// so values such as instance identifiers can never be read as expressions or field paths. No decision
/// depends on the server's clock: leases and registrations record the heartbeat that wrote them.
/// </remarks>
internal sealed class MongoDbLeaderElectionPersistence : ILeaderElectionPersistence
{
    private readonly IMongoCollection<LeaderDocument> leaderCollection;
    private readonly IMongoCollection<BsonDocument> leaderDocuments;
    private readonly IMongoCollection<NodeDocument> nodeCollection;
    private readonly IMongoCollection<BsonDocument> nodeDocuments;
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
        leaderDocuments = AsBsonDocuments(leaderCollection);
        nodeDocuments = AsBsonDocuments(nodeCollection);
    }

    /// <inheritdoc />
    public async Task<HeartbeatResult> HeartbeatAsync(
        HeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nodeId = ToBinary(request.NodeId);

        // 1. Atomic compare-and-swap on the service's lease, unless the heartbeat does not claim it
        var lease = (request.ClaimsLease ? await CompareAndSwapLeaseAsync(request, nodeId, cancellationToken).ConfigureAwait(false) : null)
            ?? await GetLeaderAsync(request.ServiceName, cancellationToken).ConfigureAwait(false);

        // 2. Refresh the node registration last, so a node whose heartbeat does not complete is not seen as
        //    alive. Such a node stops its daemons, and the leader must be able to move them once its
        //    registration goes unrenewed.
        await RegisterAsync(request, nodeId, cancellationToken).ConfigureAwait(false);

        return lease is { } held
            ? new HeartbeatResult(request.ClaimsLease && held.NodeId == request.NodeId, held.NodeId, held.HeartbeatId, held.LeaseDuration)
            : new HeartbeatResult(false, Guid.Empty, Guid.Empty, TimeSpan.Zero);
    }

    /// <inheritdoc />
    public async Task ResignLeadershipAsync(
        string serviceName,
        Guid nodeId,
        IReadOnlyCollection<Guid> heartbeatIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(heartbeatIds);

        try
        {
            await leaderDocuments.DeleteOneAsync(
                new BsonDocument
                {
                    { "serviceName", serviceName },
                    { "nodeId", ToBinary(nodeId) },
                    { "heartbeatId", new BsonDocument("$in", new BsonArray(heartbeatIds.Select(ToBinary))) },
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to resign leadership for node {NodeId}", nodeId);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> RemoveNodesAsync(
        string serviceName,
        IReadOnlyCollection<NodeRegistration> nodes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        // Each registration is removed on its own, all at once, and reported only once its removal is known: a
        // removal that fails or runs out of time is not reported, and the caller tries it again
        var removals = await Task.WhenAll(nodes.Select(async node =>
        {
            try
            {
                var result = await nodeDocuments.DeleteOneAsync(
                    new BsonDocument
                    {
                        { "_id", ToBinary(node.NodeId) },
                        { "serviceName", serviceName },
                        { "heartbeatId", ToBinary(node.HeartbeatId) },
                    },
                    cancellationToken).ConfigureAwait(false);
                return result.DeletedCount > 0 ? node.NodeId : (Guid?)null;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to remove the registration of node {NodeId}", node.NodeId);
                return null;
            }
        })).ConfigureAwait(false);

        return [.. removals.OfType<Guid>()];
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
    public async Task<IReadOnlyList<NodeRegistration>> GetNodesAsync(
        string serviceName,
        CancellationToken cancellationToken)
    {
        // Through the index on the service name alone, whatever other indexes the collection has: a scan of one keyed
        // on a field heartbeats rewrite can miss a node that renewed during it
        var nodes = await nodeCollection
            .Find(
                Builders<NodeDocument>.Filter.Eq(x => x.ServiceName, serviceName),
                new FindOptions { Hint = MongoDbEmitBuilderExtensions.NodeServiceNameIndex })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return nodes.ConvertAll(n => new NodeRegistration(n.NodeId, n.DaemonIds, n.HeartbeatId, n.RegistrationTtl));
    }

    private Task RegisterAsync(HeartbeatRequest request, BsonBinaryData nodeId, CancellationToken cancellationToken)
    {
        var nodeUpdate = Pipeline(new BsonDocument("$set", new BsonDocument
        {
            { "serviceName", Literal(request.ServiceName) },
            { "instanceId", Literal(request.InstanceId) },
            { "daemonIds", Literal(new BsonArray(request.DaemonIds)) },
            { "startedAt", new BsonDocument("$ifNull", new BsonArray { "$startedAt", "$$NOW" }) },
            { "lastSeenAt", "$$NOW" },
            { "heartbeatId", ToBinary(request.HeartbeatId) },
            { "registrationTtl", Literal((long)request.NodeTtl.TotalMilliseconds) },
        }));

        return nodeDocuments.UpdateOneAsync(
            new BsonDocument("_id", nodeId),
            nodeUpdate,
            new UpdateOptions { IsUpsert = true },
            cancellationToken);
    }

    /// <summary>
    /// Acquires the service's lease when it has none, renews it when <see cref="HeartbeatRequest.NodeId"/>
    /// holds it, or takes it over when it is still recorded with
    /// <see cref="HeartbeatRequest.TakeOverLeaseHeartbeatId"/>, in one atomic operation. Returns the lease after
    /// the operation, or <c>null</c> when a concurrent upsert created the document first.
    /// </summary>
    private async Task<(Guid NodeId, Guid HeartbeatId, TimeSpan LeaseDuration)?> CompareAndSwapLeaseAsync(
        HeartbeatRequest request,
        BsonBinaryData nodeId,
        CancellationToken cancellationToken)
    {
        // $expr is not permitted in upsert filter predicates, so the decision lives in the pipeline. An upsert
        // that inserts runs it on a document without a holder.
        BsonArray obtains =
        [
            new BsonDocument("$eq", new BsonArray { new BsonDocument("$type", "$nodeId"), "missing" }),
            new BsonDocument("$eq", new BsonArray { "$nodeId", nodeId }),
        ];
        if (request.TakeOverLeaseHeartbeatId is { } unrenewed)
        {
            obtains.Add(new BsonDocument("$eq", new BsonArray { "$heartbeatId", ToBinary(unrenewed) }));
        }

        var obtain = new BsonDocument("$or", obtains);
        var update = Pipeline(new BsonDocument("$set", new BsonDocument
        {
            { "nodeId", new BsonDocument("$cond", new BsonArray { obtain, nodeId, "$nodeId" }) },
            { "heartbeatId", new BsonDocument("$cond", new BsonArray { obtain, ToBinary(request.HeartbeatId), "$heartbeatId" }) },
            {
                "leaseDuration", new BsonDocument("$cond", new BsonArray
                {
                    obtain,
                    Literal((long)request.LeaseDuration.TotalMilliseconds),
                    "$leaseDuration",
                })
            },
        }));

        try
        {
            var lease = await leaderDocuments.FindOneAndUpdateAsync(
                new BsonDocument("serviceName", request.ServiceName),
                update,
                new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After },
                cancellationToken).ConfigureAwait(false);

            return (
                lease["nodeId"].AsBsonBinaryData.ToGuid(GuidRepresentation.Standard),
                lease["heartbeatId"].AsBsonBinaryData.ToGuid(GuidRepresentation.Standard),
                TimeSpan.FromMilliseconds(lease["leaseDuration"].ToInt64()));
        }
        catch (MongoCommandException ex) when (ex.Code == 11000)
        {
            return null;
        }
    }

    private async Task<(Guid NodeId, Guid HeartbeatId, TimeSpan LeaseDuration)?> GetLeaderAsync(string serviceName, CancellationToken cancellationToken)
    {
        var lease = await leaderCollection
            .Find(Builders<LeaderDocument>.Filter.Eq(x => x.ServiceName, serviceName))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return lease is null ? null : (lease.NodeId, lease.HeartbeatId, lease.LeaseDuration);
    }

    private static PipelineDefinition<BsonDocument, BsonDocument> Pipeline(BsonDocument stage) =>
        new EmptyPipelineDefinition<BsonDocument>().AppendStage<BsonDocument, BsonDocument, BsonDocument>(stage);

    private static BsonDocument Literal(BsonValue value) => new("$literal", value);

    private static BsonBinaryData ToBinary(Guid value) => new(value, GuidRepresentation.Standard);

    private static IMongoCollection<BsonDocument> AsBsonDocuments<TDocument>(IMongoCollection<TDocument> collection) =>
        collection.Database.GetCollection<BsonDocument>(collection.CollectionNamespace.CollectionName, collection.Settings);
}

namespace Emit.MongoDB;

using Emit.Abstractions.Daemon;
using Emit.MongoDB.Configuration;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.Models;
using global::MongoDB.Bson;
using global::MongoDB.Driver;

/// <summary>
/// MongoDB implementation of <see cref="IDaemonAssignmentPersistence"/>.
/// </summary>
/// <remarks>
/// Each service and daemon identifier pair has at most one document.
/// </remarks>
internal sealed class MongoDbDaemonAssignmentPersistence : IDaemonAssignmentPersistence
{
    private static readonly FilterDefinitionBuilder<DaemonAssignmentDocument> Filter = Builders<DaemonAssignmentDocument>.Filter;

    private static readonly FilterDefinition<DaemonAssignmentDocument> NotReleased =
        Filter.Ne(x => x.State, DaemonAssignmentDocument.ReleasedState);

    // A released document keeps its generation, moved past the last one handed out, so generations never repeat
    private static readonly UpdateDefinition<DaemonAssignmentDocument> Release = Builders<DaemonAssignmentDocument>.Update
        .Set(x => x.State, DaemonAssignmentDocument.ReleasedState)
        .Inc(x => x.Generation, 1L);

    private readonly IMongoCollection<DaemonAssignmentDocument> collection;

    public MongoDbDaemonAssignmentPersistence(MongoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        collection = context.DaemonAssignmentCollection;
    }

    /// <inheritdoc />
    public async Task<DaemonAssignment?> AssignAsync(
        string serviceName,
        string daemonId,
        Guid nodeId,
        DaemonAssignment? expected,
        CancellationToken cancellationToken)
    {
        var update = new EmptyPipelineDefinition<DaemonAssignmentDocument>()
            .AppendStage<DaemonAssignmentDocument, DaemonAssignmentDocument, DaemonAssignmentDocument>(new BsonDocument("$set", new BsonDocument
            {
                { "assignedNodeId", new BsonBinaryData(nodeId, GuidRepresentation.Standard) },
                { "generation", new BsonDocument("$add", new BsonArray { new BsonDocument("$ifNull", new BsonArray { "$generation", 0L }), 1L }) },
                { "state", nameof(DaemonAssignmentState.Assigning) },
                { "assignedAt", "$$NOW" },
                { "fencingKey", new BsonDocument("$ifNull", new BsonArray { "$fencingKey", new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard) }) },
            }));

        // A first assignment matches only a released document, or upserts a new one; an existing assignment
        // makes the insert fail on the unique service and daemon index. A reassignment only matches the document as read.
        var filter = expected is null
            ? Filter.And(ServiceDaemon(serviceName, daemonId), Filter.Eq(x => x.State, DaemonAssignmentDocument.ReleasedState))
            : Filter.And(
                ServiceDaemon(serviceName, daemonId),
                Filter.Eq(x => x.Generation, expected.Generation),
                Filter.Eq(x => x.State, expected.State.ToString()));

        try
        {
            var result = await collection.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<DaemonAssignmentDocument>
                {
                    IsUpsert = expected is null,
                    ReturnDocument = ReturnDocument.After,
                },
                cancellationToken).ConfigureAwait(false);

            return result is not null ? ToAssignment(result) : null;
        }
        catch (MongoCommandException ex) when (ex.Code == 11000)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<DaemonAssignment?> RevokeAsync(
        string serviceName,
        string daemonId,
        long expectedGeneration,
        CancellationToken cancellationToken)
    {
        var update = new EmptyPipelineDefinition<DaemonAssignmentDocument>()
            .AppendStage<DaemonAssignmentDocument, DaemonAssignmentDocument, DaemonAssignmentDocument>(new BsonDocument("$set", new BsonDocument
            {
                { "generation", new BsonDocument("$add", new BsonArray { "$generation", 1L }) },
                { "state", nameof(DaemonAssignmentState.Revoking) },
            }));

        var result = await collection.FindOneAndUpdateAsync(
            Filter.And(ServiceDaemon(serviceName, daemonId), Filter.Eq(x => x.Generation, expectedGeneration), NotReleased),
            update,
            new FindOneAndUpdateOptions<DaemonAssignmentDocument>
            {
                ReturnDocument = ReturnDocument.After,
            },
            cancellationToken).ConfigureAwait(false);

        return result is not null ? ToAssignment(result) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DaemonAssignment>> GetAllAssignmentsAsync(
        string serviceName,
        CancellationToken cancellationToken)
    {
        var documents = await collection
            .Find(
                Filter.And(Filter.Eq(x => x.ServiceName, serviceName), NotReleased),
                new FindOptions { Hint = MongoDbEmitBuilderExtensions.AssignmentServiceNameDaemonIdIndex })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return documents.ConvertAll(ToAssignment);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DaemonAssignment>> GetNodeAssignmentsAsync(
        string serviceName,
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        var filter = Filter.And(
            Filter.Eq(x => x.ServiceName, serviceName),
            Filter.Eq(x => x.AssignedNodeId, nodeId),
            NotReleased);

        var documents = await collection
            .Find(filter)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return documents.ConvertAll(ToAssignment);
    }

    /// <inheritdoc />
    public async Task<bool> AcknowledgeAsync(
        string serviceName,
        string daemonId,
        long generation,
        CancellationToken cancellationToken)
    {
        var filter = Filter.And(
            ServiceDaemon(serviceName, daemonId),
            Filter.Eq(x => x.Generation, generation),
            Filter.Eq(x => x.State, nameof(DaemonAssignmentState.Assigning)));

        var update = Builders<DaemonAssignmentDocument>.Update
            .Set(x => x.State, nameof(DaemonAssignmentState.Active));

        var result = await collection.UpdateOneAsync(
            filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.ModifiedCount > 0;
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmDrainAsync(
        string serviceName,
        string daemonId,
        long generation,
        CancellationToken cancellationToken)
    {
        var filter = Filter.And(
            ServiceDaemon(serviceName, daemonId),
            Filter.Eq(x => x.Generation, generation),
            Filter.Eq(x => x.State, nameof(DaemonAssignmentState.Revoking)));

        var result = await collection.UpdateOneAsync(filter, Release, cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.ModifiedCount > 0;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(
        string serviceName,
        string daemonId,
        long generation,
        CancellationToken cancellationToken)
    {
        var filter = Filter.And(
            ServiceDaemon(serviceName, daemonId),
            Filter.Eq(x => x.Generation, generation),
            NotReleased);

        var result = await collection.UpdateOneAsync(filter, Release, cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.ModifiedCount > 0;
    }

    private static FilterDefinition<DaemonAssignmentDocument> ServiceDaemon(string serviceName, string daemonId) =>
        Filter.And(
            Filter.Eq(x => x.ServiceName, serviceName),
            Filter.Eq(x => x.DaemonId, daemonId));

    private static DaemonAssignment ToAssignment(DaemonAssignmentDocument doc) =>
        new(
            doc.DaemonId,
            doc.AssignedNodeId,
            doc.Generation,
            Enum.Parse<DaemonAssignmentState>(doc.State),
            doc.AssignedAt,
            doc.FencingKey);
}

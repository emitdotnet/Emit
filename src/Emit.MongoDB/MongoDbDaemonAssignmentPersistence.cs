namespace Emit.MongoDB;

using Emit.Abstractions.Daemon;
using Emit.MongoDB.Configuration;
using Emit.MongoDB.Models;
using global::MongoDB.Driver;

/// <summary>
/// MongoDB implementation of <see cref="IDaemonAssignmentPersistence"/>.
/// </summary>
internal sealed class MongoDbDaemonAssignmentPersistence : IDaemonAssignmentPersistence
{
    private readonly IMongoCollection<DaemonAssignmentDocument> collection;

    public MongoDbDaemonAssignmentPersistence(MongoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        collection = context.DaemonAssignmentCollection;
    }

    /// <inheritdoc />
    public async Task<DaemonAssignment> AssignAsync(
        string daemonId,
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<DaemonAssignmentDocument>.Filter.Eq(x => x.DaemonId, daemonId);

        var pipeline = new EmptyPipelineDefinition<DaemonAssignmentDocument>()
            .AppendStage<DaemonAssignmentDocument, DaemonAssignmentDocument, DaemonAssignmentDocument>(
                $"{{ $set: {{ assignedNodeId: UUID('{nodeId}'), generation: {{ $add: [{{ $ifNull: ['$generation', 0] }}, 1] }}, state: 'Assigning', assignedAt: '$$NOW', drainDeadline: null }} }}");

        var result = await collection.FindOneAndUpdateAsync(
            filter,
            pipeline,
            new FindOneAndUpdateOptions<DaemonAssignmentDocument>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken).ConfigureAwait(false);

        return ToAssignment(result!);
    }

    /// <inheritdoc />
    public async Task<DaemonAssignment?> RevokeAsync(
        string daemonId,
        TimeSpan drainTimeout,
        CancellationToken cancellationToken)
    {
        var drainMs = (long)drainTimeout.TotalMilliseconds;

        var filter = Builders<DaemonAssignmentDocument>.Filter.Eq(x => x.DaemonId, daemonId);

        var pipeline = new EmptyPipelineDefinition<DaemonAssignmentDocument>()
            .AppendStage<DaemonAssignmentDocument, DaemonAssignmentDocument, DaemonAssignmentDocument>(
                $"{{ $set: {{ generation: {{ $add: ['$generation', 1] }}, state: 'Revoking', drainDeadline: {{ $add: ['$$NOW', {drainMs}] }} }} }}");

        var result = await collection.FindOneAndUpdateAsync(
            filter,
            pipeline,
            new FindOneAndUpdateOptions<DaemonAssignmentDocument>
            {
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken).ConfigureAwait(false);

        return result is not null ? ToAssignment(result) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DaemonAssignment>> GetAllAssignmentsAsync(
        CancellationToken cancellationToken)
    {
        var documents = await collection
            .Find(Builders<DaemonAssignmentDocument>.Filter.Empty)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return documents.Select(ToAssignment).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DaemonAssignment>> GetNodeAssignmentsAsync(
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<DaemonAssignmentDocument>.Filter.Eq(x => x.AssignedNodeId, nodeId);

        var documents = await collection
            .Find(filter)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return documents.Select(ToAssignment).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> AcknowledgeAsync(
        string daemonId,
        long generation,
        CancellationToken cancellationToken)
    {
        var filter = Builders<DaemonAssignmentDocument>.Filter.And(
            Builders<DaemonAssignmentDocument>.Filter.Eq(x => x.DaemonId, daemonId),
            Builders<DaemonAssignmentDocument>.Filter.Eq(x => x.Generation, generation),
            Builders<DaemonAssignmentDocument>.Filter.Eq(x => x.State, nameof(DaemonAssignmentState.Assigning)));

        var update = Builders<DaemonAssignmentDocument>.Update
            .Set(x => x.State, nameof(DaemonAssignmentState.Active));

        var result = await collection.UpdateOneAsync(
            filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.ModifiedCount > 0;
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmDrainAsync(
        string daemonId,
        long generation,
        CancellationToken cancellationToken)
    {
        var filter = Builders<DaemonAssignmentDocument>.Filter.And(
            Builders<DaemonAssignmentDocument>.Filter.Eq(x => x.DaemonId, daemonId),
            Builders<DaemonAssignmentDocument>.Filter.Eq(x => x.Generation, generation),
            Builders<DaemonAssignmentDocument>.Filter.Eq(x => x.State, nameof(DaemonAssignmentState.Revoking)));

        var result = await collection.DeleteOneAsync(
            filter, cancellationToken).ConfigureAwait(false);

        return result.DeletedCount > 0;
    }

    private static DaemonAssignment ToAssignment(DaemonAssignmentDocument doc)
    {
        var state = Enum.Parse<DaemonAssignmentState>(doc.State);

        return new DaemonAssignment(
            doc.DaemonId,
            doc.AssignedNodeId,
            doc.Generation,
            state,
            doc.AssignedAt,
            doc.DrainDeadline);
    }
}

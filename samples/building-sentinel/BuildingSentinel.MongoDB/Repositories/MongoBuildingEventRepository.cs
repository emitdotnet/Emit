namespace BuildingSentinel.MongoDB.Repositories;

using BuildingSentinel.Common.Domain;
using BuildingSentinel.Common.Repositories;
using Emit.Abstractions;
using Emit.MongoDB;
using Emit.MongoDB.Configuration;
using global::MongoDB.Bson.Serialization.Attributes;
using global::MongoDB.Driver;

internal sealed class MongoBuildingEventRepository(
    MongoDbContext context,
    IEmitContext emitContext) : IBuildingEventRepository
{
    private readonly IMongoCollection<BuildingEventDocument> collection =
        context.Database.GetCollection<BuildingEventDocument>("building_events");

    public async Task InsertAsync(BuildingEvent evt, CancellationToken cancellationToken = default)
    {
        var doc = new BuildingEventDocument
        {
            DeviceId = evt.DeviceId,
            EventType = evt.EventType,
            Location = evt.Location,
            OccurredAt = evt.OccurredAt,
            Metadata = evt.Metadata?.ToDictionary(k => k.Key, v => v.Value)
        };

        var session = GetSession();
        await collection.InsertOneAsync(session, doc, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private global::MongoDB.Driver.IClientSessionHandle GetSession()
    {
        if (emitContext.Transaction is IMongoTransactionContext mongoTx)
            return mongoTx.Session;

        throw new InvalidOperationException(
            "A MongoDB transaction context is required. Call BeginMongoTransactionAsync before saving.");
    }
}

internal sealed class BuildingEventDocument
{
    [BsonId] public global::MongoDB.Bson.ObjectId Id { get; set; }
    public string DeviceId { get; set; } = default!;
    public string EventType { get; set; } = default!;
    public string Location { get; set; } = default!;
    public DateTimeOffset OccurredAt { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

namespace BuildingSentinel.MongoDB.Repositories;

using BuildingSentinel.Common.Domain;
using BuildingSentinel.Common.Repositories;
using global::MongoDB.Bson.Serialization.Attributes;
using global::MongoDB.Driver;

internal sealed class MongoDeviceHeartbeatRepository(global::MongoDB.Driver.IMongoDatabase database) : IDeviceHeartbeatRepository
{
    private readonly global::MongoDB.Driver.IMongoCollection<HeartbeatDocument> collection =
        database.GetCollection<HeartbeatDocument>("device_heartbeats");

    public async Task UpsertHeartbeatAsync(
        string deviceId,
        DateTimeOffset seenAt,
        CancellationToken cancellationToken = default)
    {
        var filter = global::MongoDB.Driver.Builders<HeartbeatDocument>.Filter.Eq(x => x.DeviceId, deviceId);

        var update = global::MongoDB.Driver.Builders<HeartbeatDocument>.Update
            .Set(x => x.LastSeenAt, seenAt)
            .Inc(x => x.EventCount, 1)
            .SetOnInsert(x => x.DeviceId, deviceId);

        var options = new global::MongoDB.Driver.UpdateOptions { IsUpsert = true };

        await collection.UpdateOneAsync(filter, update, options, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DeviceHeartbeat>> GetSilentDevicesAsync(
        TimeSpan silentFor,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - silentFor;
        var filter = global::MongoDB.Driver.Builders<HeartbeatDocument>.Filter.Lt(x => x.LastSeenAt, cutoff);

        var docs = await collection.Find(filter).ToListAsync(cancellationToken).ConfigureAwait(false);

        return docs.Select(d => new DeviceHeartbeat
        {
            DeviceId = d.DeviceId,
            LastSeenAt = d.LastSeenAt,
            EventCount = d.EventCount
        }).ToList();
    }
}

internal sealed class HeartbeatDocument
{
    [BsonId] public global::MongoDB.Bson.ObjectId Id { get; set; }
    public string DeviceId { get; set; } = default!;
    public DateTimeOffset LastSeenAt { get; set; }
    public long EventCount { get; set; }
}

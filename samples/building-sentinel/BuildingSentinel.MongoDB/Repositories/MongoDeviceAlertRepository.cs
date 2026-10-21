namespace BuildingSentinel.MongoDB.Repositories;

using BuildingSentinel.Common.Domain;
using BuildingSentinel.Common.Repositories;
using Emit.MongoDB;
using Emit.MongoDB.Configuration;
using global::MongoDB.Bson.Serialization.Attributes;
using global::MongoDB.Driver;

internal sealed class MongoDeviceAlertRepository(
    MongoDbContext context,
    IMongoSessionAccessor sessionAccessor) : IDeviceAlertRepository
{
    private readonly IMongoCollection<DaemonFenceDocument> fences =
        context.Database.GetCollection<DaemonFenceDocument>("daemon_fences");

    private readonly IMongoCollection<DeviceOfflineAlertDocument> alerts =
        context.Database.GetCollection<DeviceOfflineAlertDocument>("device_offline_alerts");

    public async Task<bool> TryRecordOfflineAsync(
        DeviceOfflineAlert alert,
        Guid fencingKey,
        long generation,
        CancellationToken cancellationToken = default)
    {
        var session = sessionAccessor.Session
            ?? throw new InvalidOperationException(
                "No active MongoDB session. Ensure a unit of work has been started (e.g., via IUnitOfWork.BeginAsync).");

        try
        {
            // The fence keeps the highest generation that wrote. A lower one matches no document, so the upsert
            // tries to insert a second fence with the same key and fails.
            await fences.UpdateOneAsync(
                session,
                f => f.FencingKey == fencingKey && f.Generation <= generation,
                Builders<DaemonFenceDocument>.Update.Set(f => f.Generation, generation),
                new UpdateOptions { IsUpsert = true },
                cancellationToken).ConfigureAwait(false);

            // One alert per silence of a device: the device going silent again later is a new one
            await alerts.InsertOneAsync(
                session,
                new DeviceOfflineAlertDocument
                {
                    Id = $"{alert.DeviceId}:{alert.LastSeenAt.UtcTicks}",
                    DeviceId = alert.DeviceId,
                    LastSeenAt = alert.LastSeenAt,
                    DetectedAt = alert.DetectedAt,
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Already alerted, or fenced by a later run; the transaction is rolled back
            return false;
        }
    }
}

internal sealed class DaemonFenceDocument
{
    [BsonId] public Guid FencingKey { get; set; }
    public long Generation { get; set; }
}

internal sealed class DeviceOfflineAlertDocument
{
    [BsonId] public string Id { get; set; } = default!;
    public string DeviceId { get; set; } = default!;
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
}

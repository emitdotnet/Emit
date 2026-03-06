namespace BuildingSentinel.PostgreSQL.Repositories;

using BuildingSentinel.Common.Domain;
using BuildingSentinel.Common.Repositories;
using BuildingSentinel.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;

internal sealed class EfDeviceHeartbeatRepository(SampleDbContext dbContext) : IDeviceHeartbeatRepository
{
    public async Task UpsertHeartbeatAsync(
        string deviceId,
        DateTimeOffset seenAt,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO device_heartbeats (device_id, last_seen_at, event_count)
            VALUES ({deviceId}, {seenAt}, 1)
            ON CONFLICT (device_id) DO UPDATE
            SET last_seen_at = EXCLUDED.last_seen_at,
                event_count  = device_heartbeats.event_count + 1
            """,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DeviceHeartbeat>> GetSilentDevicesAsync(
        TimeSpan silentFor,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - silentFor;

        return await dbContext.DeviceHeartbeats
            .AsNoTracking()
            .Where(x => x.LastSeenAt < cutoff)
            .Select(x => new DeviceHeartbeat
            {
                DeviceId = x.DeviceId,
                LastSeenAt = x.LastSeenAt,
                EventCount = x.EventCount
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

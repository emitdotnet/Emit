namespace BuildingSentinel.PostgreSQL.Repositories;

using BuildingSentinel.Common.Domain;
using BuildingSentinel.Common.Repositories;
using Microsoft.EntityFrameworkCore;

internal sealed class EfDeviceAlertRepository(SampleDbContext dbContext) : IDeviceAlertRepository
{
    public async Task<bool> TryRecordOfflineAsync(
        DeviceOfflineAlert alert,
        Guid fencingKey,
        long generation,
        CancellationToken cancellationToken = default)
    {
        // The fence keeps the highest generation that wrote; a lower one updates no row
        var fenced = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO daemon_fences (fencing_key, generation) VALUES ({fencingKey}, {generation})
            ON CONFLICT (fencing_key) DO UPDATE SET generation = EXCLUDED.generation
            WHERE daemon_fences.generation <= EXCLUDED.generation
            """,
            cancellationToken).ConfigureAwait(false);
        if (fenced == 0)
        {
            return false;
        }

        // One alert per silence of a device: the device going silent again later is a new one
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO device_offline_alerts (alert_id, device_id, last_seen_at, detected_at)
            VALUES ({$"{alert.DeviceId}:{alert.LastSeenAt.UtcTicks}"}, {alert.DeviceId}, {alert.LastSeenAt}, {alert.DetectedAt})
            ON CONFLICT (alert_id) DO NOTHING
            """,
            cancellationToken).ConfigureAwait(false);

        return inserted == 1;
    }
}

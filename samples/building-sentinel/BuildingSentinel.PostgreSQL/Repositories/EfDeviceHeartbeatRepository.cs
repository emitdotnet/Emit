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
        var entity = await dbContext.DeviceHeartbeats
            .FindAsync([deviceId], cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = new DeviceHeartbeatEntity
            {
                DeviceId = deviceId,
                LastSeenAt = seenAt,
                EventCount = 1,
            };
            dbContext.DeviceHeartbeats.Add(entity);
        }
        else
        {
            entity.LastSeenAt = seenAt;
            entity.EventCount++;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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

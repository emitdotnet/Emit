namespace BuildingSentinel.PostgreSQL.Repositories;

using BuildingSentinel.Common.Domain;
using BuildingSentinel.Common.Repositories;
using BuildingSentinel.PostgreSQL.Entities;

internal sealed class EfAlertRepository(SampleDbContext dbContext) : IAlertRepository
{
    public async Task<AccessDenialAlert> IncrementDenialAsync(
        string badgeId,
        int threshold,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var entity = await dbContext.AccessDenialAlerts
            .FindAsync([badgeId], cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = new AccessDenialAlertEntity
            {
                BadgeId = badgeId,
                DenialCount = 1,
                AlarmRaised = false,
                UpdatedAt = now,
            };
            dbContext.AccessDenialAlerts.Add(entity);
        }
        else
        {
            entity.DenialCount++;
            entity.UpdatedAt = now;
        }

        if (entity.DenialCount >= threshold && !entity.AlarmRaised)
        {
            entity.AlarmRaised = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new AccessDenialAlert
        {
            BadgeId = entity.BadgeId,
            DenialCount = entity.DenialCount,
            AlarmRaised = entity.AlarmRaised,
            UpdatedAt = entity.UpdatedAt,
        };
    }
}

namespace BuildingSentinel.PostgreSQL.Repositories;

using BuildingSentinel.Common.Domain;
using BuildingSentinel.Common.Repositories;
using BuildingSentinel.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;

internal sealed class EfAlertRepository(SampleDbContext dbContext) : IAlertRepository
{
    public async Task<AccessDenialAlert> IncrementDenialAsync(
        string badgeId,
        int threshold,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO access_denial_alerts (badge_id, denial_count, alarm_raised, updated_at)
            VALUES ({badgeId}, 1, false, {now})
            ON CONFLICT (badge_id) DO UPDATE
            SET denial_count = access_denial_alerts.denial_count + 1,
                updated_at   = EXCLUDED.updated_at
            """,
            cancellationToken).ConfigureAwait(false);

        var entity = await dbContext.AccessDenialAlerts
            .AsNoTracking()
            .SingleAsync(x => x.BadgeId == badgeId, cancellationToken)
            .ConfigureAwait(false);

        if (entity.DenialCount >= threshold && !entity.AlarmRaised)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE access_denial_alerts SET alarm_raised = true WHERE badge_id = {badgeId} AND alarm_raised = false",
                cancellationToken).ConfigureAwait(false);

            entity.AlarmRaised = true;
        }

        return new AccessDenialAlert
        {
            BadgeId = entity.BadgeId,
            DenialCount = entity.DenialCount,
            AlarmRaised = entity.AlarmRaised,
            UpdatedAt = entity.UpdatedAt
        };
    }
}

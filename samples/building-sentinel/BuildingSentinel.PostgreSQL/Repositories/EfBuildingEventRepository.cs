namespace BuildingSentinel.PostgreSQL.Repositories;

using BuildingSentinel.Common.Domain;
using BuildingSentinel.Common.Repositories;
using BuildingSentinel.PostgreSQL.Entities;

internal sealed class EfBuildingEventRepository(SampleDbContext dbContext) : IBuildingEventRepository
{
    public async Task SaveAsync(BuildingEvent evt, CancellationToken cancellationToken = default)
    {
        dbContext.BuildingEvents.Add(new BuildingEventEntity
        {
            DeviceId = evt.DeviceId,
            EventType = evt.EventType,
            Location = evt.Location,
            OccurredAt = evt.OccurredAt
        });

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

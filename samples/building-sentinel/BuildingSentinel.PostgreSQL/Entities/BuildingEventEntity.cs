namespace BuildingSentinel.PostgreSQL.Entities;

public sealed class BuildingEventEntity
{
    public long Id { get; set; }
    public string DeviceId { get; set; } = default!;
    public string EventType { get; set; } = default!;
    public string Location { get; set; } = default!;
    public DateTimeOffset OccurredAt { get; set; }
}

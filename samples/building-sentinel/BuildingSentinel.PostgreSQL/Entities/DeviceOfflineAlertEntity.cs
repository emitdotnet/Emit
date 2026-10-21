namespace BuildingSentinel.PostgreSQL.Entities;

public sealed class DeviceOfflineAlertEntity
{
    public string AlertId { get; set; } = default!;
    public string DeviceId { get; set; } = default!;
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
}

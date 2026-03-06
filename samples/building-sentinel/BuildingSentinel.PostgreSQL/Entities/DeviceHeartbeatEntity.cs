namespace BuildingSentinel.PostgreSQL.Entities;

public sealed class DeviceHeartbeatEntity
{
    public string DeviceId { get; set; } = default!;
    public DateTimeOffset LastSeenAt { get; set; }
    public long EventCount { get; set; }
}

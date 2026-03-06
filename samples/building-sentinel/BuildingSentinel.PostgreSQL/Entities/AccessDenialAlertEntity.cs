namespace BuildingSentinel.PostgreSQL.Entities;

public sealed class AccessDenialAlertEntity
{
    public string BadgeId { get; set; } = default!;
    public int DenialCount { get; set; }
    public bool AlarmRaised { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

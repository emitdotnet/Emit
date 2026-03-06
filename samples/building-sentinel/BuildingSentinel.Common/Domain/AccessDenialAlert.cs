namespace BuildingSentinel.Common.Domain;

/// <summary>
/// Represents the running alarm state for a badge that has triggered repeated access-denial events.
/// </summary>
public sealed record AccessDenialAlert
{
    /// <summary>The badge or device identifier that caused the denials.</summary>
    public string BadgeId { get; init; } = default!;

    /// <summary>Total number of consecutive access denials recorded.</summary>
    public int DenialCount { get; init; }

    /// <summary>Indicates that the denial count has crossed the alarm threshold.</summary>
    public bool AlarmRaised { get; init; }

    /// <summary>UTC timestamp of the most recent denial.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

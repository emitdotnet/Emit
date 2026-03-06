namespace BuildingSentinel.Common.Domain;

/// <summary>
/// Tracks the last-seen activity of a device for liveness monitoring.
/// </summary>
public sealed record DeviceHeartbeat
{
    /// <summary>The unique device identifier.</summary>
    public string DeviceId { get; init; } = default!;

    /// <summary>UTC timestamp of the most recent event received from this device.</summary>
    public DateTimeOffset LastSeenAt { get; init; }

    /// <summary>Cumulative number of events received from this device.</summary>
    public long EventCount { get; init; }
}

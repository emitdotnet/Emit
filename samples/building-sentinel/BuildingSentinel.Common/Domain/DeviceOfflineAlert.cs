namespace BuildingSentinel.Common.Domain;

/// <summary>
/// Raised when a device has gone silent: no event received from it for longer than the configured threshold.
/// </summary>
public sealed record DeviceOfflineAlert
{
    /// <summary>The device that went silent.</summary>
    public string DeviceId { get; init; } = default!;

    /// <summary>UTC timestamp of the last event received from the device.</summary>
    public DateTimeOffset LastSeenAt { get; init; }

    /// <summary>UTC timestamp at which the silence was detected.</summary>
    public DateTimeOffset DetectedAt { get; init; }
}

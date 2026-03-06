namespace BuildingSentinel.Common.Domain;

/// <summary>
/// Represents a raw event emitted by a building sensor or access-control reader.
/// </summary>
public sealed record BuildingEvent
{
    /// <summary>The unique identifier of the physical device that raised the event.</summary>
    public string DeviceId { get; init; } = default!;

    /// <summary>The event classification (e.g. <c>access.granted</c>, <c>access.denied</c>, <c>motion.detected</c>).</summary>
    public string EventType { get; init; } = default!;

    /// <summary>Human-readable description of the physical location of the device.</summary>
    public string Location { get; init; } = default!;

    /// <summary>UTC timestamp when the event was recorded by the device.</summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>Optional key-value pairs carrying provider-specific supplementary data.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

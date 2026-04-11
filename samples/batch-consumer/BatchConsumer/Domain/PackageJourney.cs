namespace BatchConsumer.Domain;

/// <summary>
/// Tracks a package's journey through the sorting facility.
/// </summary>
public sealed record PackageJourney(
    string Barcode,
    string FacilityId,
    int CurrentLane,
    string DestinationZip,
    int ScanCount,
    bool Rerouted,
    DateTimeOffset LastSeenAt);

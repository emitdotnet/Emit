namespace BatchConsumer.Domain;

/// <summary>
/// A scan event from a conveyor belt barcode reader.
/// </summary>
public sealed record PackageScan(
    string Barcode,
    string FacilityId,
    int Lane,
    string DestinationZip,
    decimal WeightKg,
    DateTimeOffset ScannedAt);

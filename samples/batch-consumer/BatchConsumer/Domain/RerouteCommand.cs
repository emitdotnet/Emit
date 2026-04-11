namespace BatchConsumer.Domain;

/// <summary>
/// A command instructing the sorting facility to reroute a mis-sorted package.
/// </summary>
public sealed record RerouteCommand(
    string Barcode,
    int CurrentLane,
    int CorrectLane,
    string FacilityId,
    DateTimeOffset DetectedAt);

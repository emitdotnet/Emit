namespace BuildingSentinel.Common.Validation;

using BuildingSentinel.Common.Domain;
using FluentValidation;

/// <summary>
/// Validates incoming <see cref="BuildingEvent"/> messages before they reach consumer handlers.
/// Rejects events with missing device identity, unknown event types, or missing location data.
/// </summary>
public sealed class BuildingEventValidator : AbstractValidator<BuildingEvent>
{
    private static readonly HashSet<string> KnownEventTypes = new(StringComparer.Ordinal)
    {
        "access.granted",
        "access.denied",
        "motion.detected"
    };

    public BuildingEventValidator()
    {
        RuleFor(e => e.DeviceId)
            .NotEmpty()
            .WithMessage("Device ID is required — the event cannot be attributed to a source.");

        RuleFor(e => e.EventType)
            .Must(t => KnownEventTypes.Contains(t))
            .WithMessage(e => $"Unknown event type '{e.EventType}' — expected one of: {string.Join(", ", KnownEventTypes)}.");

        RuleFor(e => e.Location)
            .NotEmpty()
            .WithMessage("Location is required — the event cannot be placed in the building.");
    }
}

namespace BatchConsumer.Validation;

using BatchConsumer.Domain;
using FluentValidation;

public sealed class PackageScanValidator : AbstractValidator<PackageScan>
{
    private static readonly HashSet<string> KnownFacilities = new(StringComparer.Ordinal)
    {
        "FAC-NORTH",
        "FAC-SOUTH",
        "FAC-EAST",
        "FAC-WEST"
    };

    public PackageScanValidator()
    {
        RuleFor(s => s.Barcode)
            .NotEmpty()
            .WithMessage("Barcode is required — the scan cannot be attributed to a package.");

        RuleFor(s => s.WeightKg)
            .GreaterThan(0)
            .WithMessage("Weight must be positive — sensor may have malfunctioned.");

        RuleFor(s => s.FacilityId)
            .Must(f => KnownFacilities.Contains(f))
            .WithMessage(s => $"Unknown facility '{s.FacilityId}' — expected one of: {string.Join(", ", KnownFacilities)}.");
    }
}

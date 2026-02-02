namespace Emit.Configuration;

using FluentValidation;

/// <summary>
/// Validates <see cref="WorkerOptions"/> configuration.
/// </summary>
/// <remarks>
/// Ensures lease duration and renewal interval are configured with safe values
/// to prevent lease expiration during normal operation.
/// </remarks>
internal sealed class WorkerOptionsValidator : AbstractValidator<WorkerOptions>
{
    /// <summary>
    /// Minimum allowed value for <see cref="WorkerOptions.LeaseDuration"/>.
    /// </summary>
    public static readonly TimeSpan MinLeaseDuration = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Minimum allowed value for <see cref="WorkerOptions.LeaseRenewalInterval"/>.
    /// </summary>
    public static readonly TimeSpan MinLeaseRenewalInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerOptionsValidator"/> class.
    /// </summary>
    public WorkerOptionsValidator()
    {
        RuleFor(x => x.LeaseDuration)
            .GreaterThanOrEqualTo(MinLeaseDuration)
            .WithMessage($"Lease duration must be at least {MinLeaseDuration.TotalSeconds} seconds.");

        RuleFor(x => x.LeaseRenewalInterval)
            .GreaterThanOrEqualTo(MinLeaseRenewalInterval)
            .WithMessage($"Lease renewal interval must be at least {MinLeaseRenewalInterval.TotalSeconds} seconds.");

        RuleFor(x => x.LeaseRenewalInterval)
            .LessThan(x => TimeSpan.FromTicks(x.LeaseDuration.Ticks / 2))
            .WithMessage("Lease renewal interval must be less than half the lease duration to ensure reliable renewal.");

        RuleFor(x => x.WorkerId)
            .MaximumLength(200)
            .When(x => x.WorkerId is not null)
            .WithMessage("Worker ID must be at most 200 characters.");
    }
}

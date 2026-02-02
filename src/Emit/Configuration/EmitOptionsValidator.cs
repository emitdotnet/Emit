namespace Emit.Configuration;

using FluentValidation;

/// <summary>
/// Validates <see cref="EmitOptions"/> configuration.
/// </summary>
/// <remarks>
/// Ensures polling interval, batch size, and group limits are configured with safe values
/// for efficient outbox processing.
/// </remarks>
internal sealed class EmitOptionsValidator : AbstractValidator<EmitOptions>
{
    /// <summary>
    /// Minimum allowed value for <see cref="EmitOptions.PollingInterval"/>.
    /// </summary>
    public static readonly TimeSpan MinPollingInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum allowed value for <see cref="EmitOptions.BatchSize"/>.
    /// </summary>
    public const int MaxBatchSize = 10000;

    /// <summary>
    /// Maximum allowed value for <see cref="EmitOptions.MaxGroupsPerCycle"/>.
    /// </summary>
    public const int MaxGroupsPerCycleLimit = 10000;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmitOptionsValidator"/> class.
    /// </summary>
    public EmitOptionsValidator()
    {
        RuleFor(x => x.PollingInterval)
            .GreaterThanOrEqualTo(MinPollingInterval)
            .WithMessage($"Polling interval must be at least {MinPollingInterval.TotalSeconds} second(s).");

        RuleFor(x => x.BatchSize)
            .GreaterThan(0)
            .WithMessage("Batch size must be greater than 0.");

        RuleFor(x => x.BatchSize)
            .LessThanOrEqualTo(MaxBatchSize)
            .WithMessage($"Batch size must be at most {MaxBatchSize}.");

        RuleFor(x => x.MaxGroupsPerCycle)
            .GreaterThan(0)
            .WithMessage("Max groups per cycle must be greater than 0.");

        RuleFor(x => x.MaxGroupsPerCycle)
            .LessThanOrEqualTo(MaxGroupsPerCycleLimit)
            .WithMessage($"Max groups per cycle must be at most {MaxGroupsPerCycleLimit}.");
    }
}

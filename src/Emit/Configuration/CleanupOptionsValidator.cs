namespace Emit.Configuration;

using FluentValidation;

/// <summary>
/// Validates <see cref="CleanupOptions"/> configuration.
/// </summary>
/// <remarks>
/// Ensures retention period and cleanup interval are configured with safe minimum values
/// to prevent excessive database load from overly aggressive cleanup.
/// </remarks>
internal sealed class CleanupOptionsValidator : AbstractValidator<CleanupOptions>
{
    /// <summary>
    /// Minimum allowed value for <see cref="CleanupOptions.RetentionPeriod"/>.
    /// </summary>
    public static readonly TimeSpan MinRetentionPeriod = TimeSpan.FromHours(1);

    /// <summary>
    /// Minimum allowed value for <see cref="CleanupOptions.CleanupInterval"/>.
    /// </summary>
    public static readonly TimeSpan MinCleanupInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Minimum allowed value for <see cref="CleanupOptions.BatchSize"/>.
    /// </summary>
    public const int MinBatchSize = 1;

    /// <summary>
    /// Maximum allowed value for <see cref="CleanupOptions.BatchSize"/>.
    /// </summary>
    public const int MaxBatchSize = 10000;

    /// <summary>
    /// Initializes a new instance of the <see cref="CleanupOptionsValidator"/> class.
    /// </summary>
    public CleanupOptionsValidator()
    {
        RuleFor(x => x.RetentionPeriod)
            .GreaterThanOrEqualTo(MinRetentionPeriod)
            .WithMessage($"Retention period must be at least {MinRetentionPeriod.TotalHours} hour(s).");

        RuleFor(x => x.CleanupInterval)
            .GreaterThanOrEqualTo(MinCleanupInterval)
            .WithMessage($"Cleanup interval must be at least {MinCleanupInterval.TotalMinutes} minutes.");

        RuleFor(x => x.BatchSize)
            .InclusiveBetween(MinBatchSize, MaxBatchSize)
            .WithMessage($"Batch size must be between {MinBatchSize} and {MaxBatchSize}.");
    }
}

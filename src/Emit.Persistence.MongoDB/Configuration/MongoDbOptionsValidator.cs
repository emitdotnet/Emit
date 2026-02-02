namespace Emit.Persistence.MongoDB.Configuration;

using FluentValidation;

/// <summary>
/// Validator for <see cref="MongoDbOptions"/>.
/// </summary>
internal sealed class MongoDbOptionsValidator : AbstractValidator<MongoDbOptions>
{
    /// <summary>
    /// Minimum allowed retention period.
    /// </summary>
    public static readonly TimeSpan MinimumRetentionPeriod = TimeSpan.FromHours(1);

    /// <summary>
    /// Initializes a new instance of the <see cref="MongoDbOptionsValidator"/> class.
    /// </summary>
    public MongoDbOptionsValidator()
    {
        RuleFor(x => x.ConnectionString)
            .NotEmpty()
            .WithMessage("MongoDB connection string is required.");

        RuleFor(x => x.DatabaseName)
            .NotEmpty()
            .WithMessage("Database name is required.");

        RuleFor(x => x.CollectionName)
            .NotEmpty()
            .WithMessage("Collection name is required.");

        RuleFor(x => x.CounterCollectionName)
            .NotEmpty()
            .WithMessage("Counter collection name is required.");

        RuleFor(x => x.LeaseCollectionName)
            .NotEmpty()
            .WithMessage("Lease collection name is required.");

        RuleFor(x => x.RetentionPeriod)
            .GreaterThanOrEqualTo(MinimumRetentionPeriod)
            .WithMessage($"Retention period must be at least {MinimumRetentionPeriod.TotalHours} hour(s).");

        RuleFor(x => x.MaxAttemptsPerEntry)
            .GreaterThan(0)
            .WithMessage("Maximum attempts per entry must be greater than zero.");
    }
}

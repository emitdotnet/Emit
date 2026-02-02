namespace Emit.Persistence.PostgreSQL.Configuration;

using FluentValidation;

/// <summary>
/// Validator for <see cref="PostgreSqlOptions"/>.
/// </summary>
internal sealed class PostgreSqlOptionsValidator : AbstractValidator<PostgreSqlOptions>
{
    /// <summary>
    /// Minimum allowed retention period.
    /// </summary>
    public static readonly TimeSpan MinimumRetentionPeriod = TimeSpan.FromHours(1);

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlOptionsValidator"/> class.
    /// </summary>
    public PostgreSqlOptionsValidator()
    {
        RuleFor(x => x.ConnectionString)
            .NotEmpty()
            .WithMessage("PostgreSQL connection string is required.");

        RuleFor(x => x.TableName)
            .NotEmpty()
            .WithMessage("Table name is required.");

        RuleFor(x => x.LeaseTableName)
            .NotEmpty()
            .WithMessage("Lease table name is required.");

        RuleFor(x => x.RetentionPeriod)
            .GreaterThanOrEqualTo(MinimumRetentionPeriod)
            .WithMessage($"Retention period must be at least {MinimumRetentionPeriod.TotalHours} hour(s).");

        RuleFor(x => x.MaxAttemptsPerEntry)
            .GreaterThan(0)
            .WithMessage("Maximum attempts per entry must be greater than zero.");
    }
}

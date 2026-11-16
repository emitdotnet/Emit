namespace Emit.EntityFrameworkCore.Configuration;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="EntityFrameworkCoreInboxOptions"/> configuration.
/// </summary>
internal sealed class EntityFrameworkCoreInboxOptionsValidator : IValidateOptions<EntityFrameworkCoreInboxOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, EntityFrameworkCoreInboxOptions options)
    {
        List<string> failures = [];

        if (options.Retention <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.Retention)} must be greater than zero.");
        }

        if (options.CleanupInterval <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.CleanupInterval)} must be greater than zero.");
        }

        if (options.CleanupBatchSize <= 0)
        {
            failures.Add($"{nameof(options.CleanupBatchSize)} must be greater than zero.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}

namespace Emit.Configuration;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="OutboxOptions"/> configuration.
/// </summary>
internal sealed class OutboxOptionsValidator : IValidateOptions<OutboxOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, OutboxOptions options)
    {
        List<string> failures = [];

        if (options.PollingInterval < ValidationConstants.MinPollingInterval)
        {
            failures.Add($"{nameof(options.PollingInterval)} must be at least {ValidationConstants.MinPollingInterval.TotalSeconds} second(s).");
        }

        if (options.BatchSize <= 0)
        {
            failures.Add($"{nameof(options.BatchSize)} must be greater than 0.");
        }
        else if (options.BatchSize > ValidationConstants.MaxBatchSize)
        {
            failures.Add($"{nameof(options.BatchSize)} must be at most {ValidationConstants.MaxBatchSize}.");
        }

        if (options.MaxGroupsPerCycle <= 0)
        {
            failures.Add($"{nameof(options.MaxGroupsPerCycle)} must be greater than 0.");
        }
        else if (options.MaxGroupsPerCycle > ValidationConstants.MaxGroupsPerCycle)
        {
            failures.Add($"{nameof(options.MaxGroupsPerCycle)} must be at most {ValidationConstants.MaxGroupsPerCycle}.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

namespace Emit.Configuration;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="DaemonOptions"/> configuration.
/// </summary>
internal sealed class DaemonOptionsValidator : IValidateOptions<DaemonOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, DaemonOptions options)
    {
        List<string> failures = [];

        if (options.AcknowledgeTimeout <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.AcknowledgeTimeout)} must be greater than zero.");
        }

        if (options.DrainTimeout <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.DrainTimeout)} must be greater than zero.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

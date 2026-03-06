namespace Emit.Configuration;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="LeaderElectionOptions"/> configuration.
/// </summary>
internal sealed class LeaderElectionOptionsValidator : IValidateOptions<LeaderElectionOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, LeaderElectionOptions options)
    {
        List<string> failures = [];

        if (options.QueryTimeout <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.QueryTimeout)} must be greater than zero.");
        }

        if (options.HeartbeatInterval <= options.QueryTimeout)
        {
            failures.Add(
                $"{nameof(options.HeartbeatInterval)} ({options.HeartbeatInterval.TotalSeconds}s) " +
                $"must be greater than {nameof(options.QueryTimeout)} ({options.QueryTimeout.TotalSeconds}s).");
        }

        if (options.LeaseDuration <= options.HeartbeatInterval + options.QueryTimeout)
        {
            failures.Add(
                $"{nameof(options.LeaseDuration)} ({options.LeaseDuration.TotalSeconds}s) " +
                $"must be greater than {nameof(options.HeartbeatInterval)} + {nameof(options.QueryTimeout)} " +
                $"({(options.HeartbeatInterval + options.QueryTimeout).TotalSeconds}s).");
        }

        if (options.NodeRegistrationTtl <= options.LeaseDuration)
        {
            failures.Add(
                $"{nameof(options.NodeRegistrationTtl)} ({options.NodeRegistrationTtl.TotalSeconds}s) " +
                $"must be greater than {nameof(options.LeaseDuration)} ({options.LeaseDuration.TotalSeconds}s).");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

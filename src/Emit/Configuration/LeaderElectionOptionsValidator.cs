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

        foreach (var (optionName, value) in new[]
        {
            (nameof(options.HeartbeatInterval), options.HeartbeatInterval),
            (nameof(options.LeaseDuration), options.LeaseDuration),
            (nameof(options.QueryTimeout), options.QueryTimeout),
            (nameof(options.NodeRegistrationTtl), options.NodeRegistrationTtl),
        })
        {
            if (value > ValidationConstants.MaxCoordinationDuration)
            {
                failures.Add($"{optionName} must not exceed {ValidationConstants.MaxCoordinationDuration.TotalDays:0.#} days.");
            }
        }

        if (options.QueryTimeout < ValidationConstants.MinQueryTimeout)
        {
            failures.Add($"{nameof(options.QueryTimeout)} must be at least {ValidationConstants.MinQueryTimeout.TotalMilliseconds}ms.");
        }

        if (options.HeartbeatInterval < ValidationConstants.MinHeartbeatInterval)
        {
            failures.Add(
                $"{nameof(options.HeartbeatInterval)} must be at least {ValidationConstants.MinHeartbeatInterval.TotalSeconds}s.");
        }

        if (options.HeartbeatInterval <= options.QueryTimeout * ValidationConstants.QueryTimeoutsPerHeartbeat)
        {
            failures.Add(
                $"{nameof(options.HeartbeatInterval)} ({options.HeartbeatInterval.TotalSeconds}s) must be greater than " +
                $"{ValidationConstants.QueryTimeoutsPerHeartbeat} times {nameof(options.QueryTimeout)} ({options.QueryTimeout.TotalSeconds}s).");
        }

        // The lease a node holds is renewed by its next heartbeat, which may take its whole timeout, once the node has
        // read its assignments; it is held a little less than recorded, for clock drift
        var renewal = options.HeartbeatInterval + options.HeartbeatTimeout + options.QueryTimeout;
        if (options.HeldLease <= renewal)
        {
            failures.Add(
                $"{nameof(options.LeaseDuration)} ({options.LeaseDuration.TotalSeconds}s), less {ValidationConstants.MaxClockDriftRate:P1} for clock drift, " +
                $"must be greater than {nameof(options.HeartbeatInterval)} + {ValidationConstants.QueriesPerHeartbeat + 1} x " +
                $"{nameof(options.QueryTimeout)} ({renewal.TotalSeconds}s).");
        }

        if (options.NodeRegistrationTtl <= options.LeaseDuration)
        {
            failures.Add(
                $"{nameof(options.NodeRegistrationTtl)} ({options.NodeRegistrationTtl.TotalSeconds}s) " +
                $"must be greater than {nameof(options.LeaseDuration)} ({options.LeaseDuration.TotalSeconds}s).");
        }

        if (string.IsNullOrWhiteSpace(options.ServiceName))
        {
            failures.Add(
                $"{nameof(options.ServiceName)} must be set. It defaults to the host's application name; " +
                $"set it explicitly when no application name is available.");
        }
        else if (options.ServiceName.Length > ValidationConstants.MaxServiceNameLength)
        {
            failures.Add(
                $"{nameof(options.ServiceName)} must be at most {ValidationConstants.MaxServiceNameLength} characters " +
                $"(was {options.ServiceName.Length}).");
        }

        if (options.InstanceId?.Length > ValidationConstants.MaxInstanceIdLength)
        {
            failures.Add(
                $"{nameof(options.InstanceId)} must be at most {ValidationConstants.MaxInstanceIdLength} characters " +
                $"(was {options.InstanceId.Length}).");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

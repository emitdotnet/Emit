namespace Emit.Configuration;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="DaemonOptions"/> configuration, including its timings against the leader election timings.
/// </summary>
internal sealed class DaemonOptionsValidator(IOptions<LeaderElectionOptions> leaderElectionOptions) : IValidateOptions<DaemonOptions>
{
    private readonly LeaderElectionOptions leaderElection = leaderElectionOptions.Value;

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, DaemonOptions options)
    {
        List<string> failures = [];

        foreach (var (optionName, value) in new[]
        {
            (nameof(options.AcknowledgeTimeout), options.AcknowledgeTimeout),
            (nameof(options.DrainTimeout), options.DrainTimeout),
            (nameof(options.FailureCooldown), options.FailureCooldown),
        })
        {
            if (value > ValidationConstants.MaxCoordinationDuration)
            {
                failures.Add($"{optionName} must not exceed {ValidationConstants.MaxCoordinationDuration.TotalDays:0.#} days.");
            }
        }

        if (options.DrainTimeout <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.DrainTimeout)} must be greater than zero.");
        }

        // A healthy node acknowledges in the tick of its next heartbeat, which may take its whole timeout, after
        // reading its assignments; it acknowledges before anything else it writes
        var acknowledgement = leaderElection.HeartbeatInterval + leaderElection.HeartbeatTimeout + (leaderElection.QueryTimeout * 2);
        if (options.AcknowledgeTimeout <= acknowledgement)
        {
            failures.Add(
                $"{nameof(options.AcknowledgeTimeout)} ({options.AcknowledgeTimeout.TotalSeconds}s) must be greater than " +
                $"{nameof(LeaderElectionOptions.HeartbeatInterval)} + {ValidationConstants.QueriesPerHeartbeat + 2} x " +
                $"{nameof(LeaderElectionOptions.QueryTimeout)} ({acknowledgement.TotalSeconds}s).");
        }

        // A node that loses the database stops its daemons when its lease would expire; they must have stopped
        // by the time its registration expires and the leader moves them, measured on a leader clock that can run
        // faster than the node's. A heartbeat interval is kept free for the fence's own timer and for a leader that
        // starts the daemon in the tick it sees the registration expire.
        var stopWindow = leaderElection.ReleaseAfter - leaderElection.LeaseDuration - leaderElection.HeartbeatInterval;
        if (options.DrainTimeout > stopWindow)
        {
            failures.Add(
                $"{nameof(options.DrainTimeout)} ({options.DrainTimeout.TotalSeconds}s) must not exceed " +
                $"{nameof(LeaderElectionOptions.NodeRegistrationTtl)}, less {ValidationConstants.MaxClockDriftRate:P1} for clock drift, - " +
                $"{nameof(LeaderElectionOptions.LeaseDuration)} - {nameof(LeaderElectionOptions.HeartbeatInterval)} ({stopWindow.TotalSeconds}s).");
        }

        if (options.MaxConsecutiveFailures < 1)
        {
            failures.Add($"{nameof(options.MaxConsecutiveFailures)} must be at least 1.");
        }

        // The node leaves the daemon out of its registrations for the cooldown. Moving it takes a heartbeat for the
        // leader to see that, one for it to revoke the daemon, one for the node to confirm the drain, and one to
        // place it elsewhere; a shorter cooldown would have the node declare it again and get it back.
        var move = (leaderElection.HeartbeatInterval + leaderElection.HeartbeatTimeout) * 4;
        if (options.FailureCooldown <= move)
        {
            failures.Add(
                $"{nameof(options.FailureCooldown)} ({options.FailureCooldown.TotalSeconds}s) must be greater than 4 x " +
                $"({nameof(LeaderElectionOptions.HeartbeatInterval)} + {ValidationConstants.QueriesPerHeartbeat} x " +
                $"{nameof(LeaderElectionOptions.QueryTimeout)}) ({move.TotalSeconds}s).");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

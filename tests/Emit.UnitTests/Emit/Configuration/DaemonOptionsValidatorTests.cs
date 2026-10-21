namespace Emit.UnitTests.Configuration;

using global::Emit.Configuration;
using Xunit;

public class DaemonOptionsValidatorTests
{
    private readonly LeaderElectionOptions leaderElection = new();
    private readonly DaemonOptionsValidator validator;

    public DaemonOptionsValidatorTests()
    {
        validator = new DaemonOptionsValidator(Microsoft.Extensions.Options.Options.Create(leaderElection));
    }

    [Fact]
    public void GivenDefaultOptions_WhenValidated_ThenSucceeds()
    {
        // Act
        var result = validator.Validate(null, new DaemonOptions());

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(nameof(DaemonOptions.AcknowledgeTimeout), 0, false)]
    [InlineData(nameof(DaemonOptions.AcknowledgeTimeout), 1, true)]
    [InlineData(nameof(DaemonOptions.DrainTimeout), 0, false)]
    [InlineData(nameof(DaemonOptions.DrainTimeout), 1, true)]
    [InlineData(nameof(DaemonOptions.FailureCooldown), 0, false)]
    [InlineData(nameof(DaemonOptions.FailureCooldown), 1, true)]
    public void GivenDurationAroundTheLongestATimerCanWait_WhenValidated_ThenOnlyALongerOneIsRejected(
        string option,
        int extraMilliseconds,
        bool rejected)
    {
        // Arrange: every duration ends up waited on by a timer, which cannot wait longer
        var options = With(option, ValidationConstants.MaxCoordinationDuration + TimeSpan.FromMilliseconds(extraMilliseconds));

        // Act
        var result = validator.Validate(null, options);

        // Assert: longer durations also break other rules, so only this option's limit is checked
        Assert.Equal(rejected, result.Failures?.Any(f => f.StartsWith($"{option} must not exceed", StringComparison.Ordinal)) ?? false);
    }

    [Fact]
    public void GivenZeroMaxConsecutiveFailures_WhenValidated_ThenFails()
    {
        // Act
        var result = validator.Validate(null, new DaemonOptions { MaxConsecutiveFailures = 0 });

        // Assert
        Assert.True(result.Failed);
        Assert.Contains(nameof(DaemonOptions.MaxConsecutiveFailures), result.FailureMessage);
    }

    [Theory]
    [InlineData(nameof(DaemonOptions.AcknowledgeTimeout), 0, false)]
    [InlineData(nameof(DaemonOptions.AcknowledgeTimeout), 1, true)]
    [InlineData(nameof(DaemonOptions.FailureCooldown), 0, false)]
    [InlineData(nameof(DaemonOptions.FailureCooldown), 1, true)]
    [InlineData(nameof(DaemonOptions.DrainTimeout), 0, false)]
    [InlineData(nameof(DaemonOptions.DrainTimeout), 1, true)]
    public void GivenDurationAroundWhatItMustOutlast_WhenValidated_ThenOnlyALongerOneIsAccepted(string option, int extraMilliseconds, bool accepted)
    {
        // Arrange: a healthy node acknowledges in the tick of its next heartbeat, after reading its assignments, a
        // daemon left out of a node's registrations takes four heartbeats to move elsewhere, and a daemon must be
        // given some time to stop
        var heartbeat = leaderElection.HeartbeatInterval + leaderElection.HeartbeatTimeout;
        var bound = option switch
        {
            nameof(DaemonOptions.AcknowledgeTimeout) => heartbeat + (leaderElection.QueryTimeout * 2),
            nameof(DaemonOptions.FailureCooldown) => heartbeat * 4,
            _ => TimeSpan.Zero,
        };
        var options = With(option, bound + TimeSpan.FromMilliseconds(extraMilliseconds));

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.Equal(accepted, result.Succeeded);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void GivenDrainTimeoutAroundTheTimeTheRegistrationOutlivesTheLeaseLessAHeartbeat_WhenValidated_ThenOnlyALongerOneIsRejected(int extraMilliseconds, bool accepted)
    {
        // Arrange: a fenced node must have stopped its daemons by the time the leader moves them, on a leader clock
        // that can run faster than the node's. A heartbeat interval is kept free for the fence's own timer and for a
        // leader that starts the daemon in the tick it sees the registration expire.
        var options = new DaemonOptions
        {
            DrainTimeout = (leaderElection.NodeRegistrationTtl * (1 - ValidationConstants.MaxClockDriftRate)) - leaderElection.LeaseDuration - leaderElection.HeartbeatInterval
                + TimeSpan.FromMilliseconds(extraMilliseconds),
        };

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.Equal(accepted, result.Succeeded);
    }

    private static DaemonOptions With(string option, TimeSpan value) =>
        option switch
        {
            nameof(DaemonOptions.AcknowledgeTimeout) => new DaemonOptions { AcknowledgeTimeout = value },
            nameof(DaemonOptions.DrainTimeout) => new DaemonOptions { DrainTimeout = value },
            nameof(DaemonOptions.FailureCooldown) => new DaemonOptions { FailureCooldown = value },
            _ => throw new ArgumentOutOfRangeException(nameof(option), option, null),
        };
}

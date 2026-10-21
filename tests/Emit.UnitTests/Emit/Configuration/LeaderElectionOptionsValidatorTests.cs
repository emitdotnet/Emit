namespace Emit.UnitTests.Configuration;

using global::Emit.Configuration;
using Xunit;

public class LeaderElectionOptionsValidatorTests
{
    private readonly LeaderElectionOptionsValidator validator = new();

    [Fact]
    public void GivenDefaultOptionsWithAServiceName_WhenValidated_ThenSucceeds()
    {
        // Act
        var result = validator.Validate(null, new LeaderElectionOptions { ServiceName = "test-service" });

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(nameof(LeaderElectionOptions.QueryTimeout), 0, false)]
    [InlineData(nameof(LeaderElectionOptions.QueryTimeout), 1, true)]
    [InlineData(nameof(LeaderElectionOptions.HeartbeatInterval), 0, false)]
    [InlineData(nameof(LeaderElectionOptions.HeartbeatInterval), 1, true)]
    [InlineData(nameof(LeaderElectionOptions.LeaseDuration), 0, false)]
    [InlineData(nameof(LeaderElectionOptions.LeaseDuration), 1, true)]
    [InlineData(nameof(LeaderElectionOptions.NodeRegistrationTtl), 0, false)]
    [InlineData(nameof(LeaderElectionOptions.NodeRegistrationTtl), 1, true)]
    public void GivenDurationAroundTheOneItMustExceed_WhenValidated_ThenOnlyALongerOneIsAccepted(
        string option,
        int extraMilliseconds,
        bool accepted)
    {
        // Arrange: a query must be allowed the time a networked database needs to answer, a heartbeat must hold its queries and the work after them, a
        // lease, held a little less for clock drift, the heartbeat that renews it and the read after it, and a
        // registration the lease
        var defaults = new LeaderElectionOptions();
        var bound = option switch
        {
            nameof(LeaderElectionOptions.QueryTimeout) => ValidationConstants.MinQueryTimeout - TimeSpan.FromMilliseconds(1),
            nameof(LeaderElectionOptions.HeartbeatInterval) => defaults.QueryTimeout * ValidationConstants.QueryTimeoutsPerHeartbeat,
            nameof(LeaderElectionOptions.LeaseDuration) => TimeSpan.FromMilliseconds(Math.Floor(
                (defaults.HeartbeatInterval + defaults.HeartbeatTimeout + defaults.QueryTimeout).TotalMilliseconds / (1 - ValidationConstants.MaxClockDriftRate))),
            _ => defaults.LeaseDuration,
        };
        var options = With(option, bound + TimeSpan.FromMilliseconds(extraMilliseconds));

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.Equal(accepted, result.Succeeded);
        if (!accepted)
        {
            Assert.All(result.Failures!, f => Assert.StartsWith($"{option} ", f, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(nameof(LeaderElectionOptions.HeartbeatInterval), 0, false)]
    [InlineData(nameof(LeaderElectionOptions.HeartbeatInterval), 1, true)]
    [InlineData(nameof(LeaderElectionOptions.LeaseDuration), 0, false)]
    [InlineData(nameof(LeaderElectionOptions.LeaseDuration), 1, true)]
    [InlineData(nameof(LeaderElectionOptions.QueryTimeout), 0, false)]
    [InlineData(nameof(LeaderElectionOptions.QueryTimeout), 1, true)]
    [InlineData(nameof(LeaderElectionOptions.NodeRegistrationTtl), 0, false)]
    [InlineData(nameof(LeaderElectionOptions.NodeRegistrationTtl), 1, true)]
    public void GivenDurationAroundTheLongestATimerCanWait_WhenValidated_ThenOnlyALongerOneIsRejected(
        string option,
        int extraMilliseconds,
        bool rejected)
    {
        // Arrange: the node fences itself, renews and times its queries with timers, which cannot wait longer
        var options = With(option, ValidationConstants.MaxCoordinationDuration + TimeSpan.FromMilliseconds(extraMilliseconds));

        // Act
        var result = validator.Validate(null, options);

        // Assert: longer durations also break other rules, so only this option's limit is checked
        Assert.Equal(rejected, result.Failures?.Any(f => f.StartsWith($"{option} must not exceed", StringComparison.Ordinal)) ?? false);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    public void GivenHeartbeatIntervalAroundTheShortestOneTimersAllowFor_WhenValidated_ThenOnlyAShorterOneIsRejected(
        int extraTicks,
        bool rejected)
    {
        // Arrange: timers fire up to an operating system timer tick late, and the lease keeps one heartbeat interval
        // in reserve for its own timer, which must stay well above that
        var options = With(nameof(LeaderElectionOptions.HeartbeatInterval), ValidationConstants.MinHeartbeatInterval + TimeSpan.FromTicks(extraTicks));

        // Act
        var result = validator.Validate(null, options);

        // Assert: such a short interval also breaks other rules, so only this limit is checked
        Assert.Equal(rejected, result.Failures?.Any(f => f.StartsWith($"{nameof(LeaderElectionOptions.HeartbeatInterval)} must be at least", StringComparison.Ordinal)) ?? false);
    }

    [Fact]
    public void GivenMultipleViolations_WhenValidated_ThenReportsEachOfThem()
    {
        // Arrange
        var options = new LeaderElectionOptions
        {
            QueryTimeout = TimeSpan.Zero,
            HeartbeatInterval = TimeSpan.Zero,
            LeaseDuration = TimeSpan.Zero,
            NodeRegistrationTtl = TimeSpan.Zero,
        };

        // Act
        var result = validator.Validate(null, options);

        // Assert: the validator reports every violation, not only the first
        Assert.True(result.Failed);
        var failedOptions = result.Failures!.Select(f => f.Split(' ')[0]).ToHashSet();
        Assert.Superset(
            new HashSet<string>
            {
                nameof(LeaderElectionOptions.QueryTimeout),
                nameof(LeaderElectionOptions.HeartbeatInterval),
                nameof(LeaderElectionOptions.LeaseDuration),
                nameof(LeaderElectionOptions.NodeRegistrationTtl),
                nameof(LeaderElectionOptions.ServiceName),
            },
            failedOptions);
    }

    [Fact]
    public void GivenBlankServiceName_WhenValidated_ThenFails()
    {
        // Arrange: a missing one is among the violations reported together
        var options = new LeaderElectionOptions { ServiceName = "   " };

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains(nameof(LeaderElectionOptions.ServiceName), result.FailureMessage);
    }

    [Theory]
    [InlineData(nameof(LeaderElectionOptions.ServiceName), 0, true)]
    [InlineData(nameof(LeaderElectionOptions.ServiceName), 1, false)]
    [InlineData(nameof(LeaderElectionOptions.InstanceId), 0, true)]
    [InlineData(nameof(LeaderElectionOptions.InstanceId), 1, false)]
    public void GivenIdentifierAroundTheMaximumLengthTheDatabaseStores_WhenValidated_ThenOnlyALongerOneIsRejected(
        string property,
        int extraCharacters,
        bool accepted)
    {
        // Arrange: a longer value would fail every heartbeat once the database rejects it
        var options = new LeaderElectionOptions { ServiceName = "service" };
        if (property == nameof(LeaderElectionOptions.ServiceName))
        {
            options.ServiceName = new string('s', ValidationConstants.MaxServiceNameLength + extraCharacters);
        }
        else
        {
            options.InstanceId = new string('i', ValidationConstants.MaxInstanceIdLength + extraCharacters);
        }

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.Equal(accepted, result.Succeeded);
        if (!accepted)
        {
            Assert.Contains(property, result.FailureMessage);
        }
    }

    private static LeaderElectionOptions With(string option, TimeSpan value) =>
        option switch
        {
            nameof(LeaderElectionOptions.HeartbeatInterval) => new LeaderElectionOptions { ServiceName = "service", HeartbeatInterval = value },
            nameof(LeaderElectionOptions.LeaseDuration) => new LeaderElectionOptions { ServiceName = "service", LeaseDuration = value },
            nameof(LeaderElectionOptions.QueryTimeout) => new LeaderElectionOptions { ServiceName = "service", QueryTimeout = value },
            nameof(LeaderElectionOptions.NodeRegistrationTtl) => new LeaderElectionOptions { ServiceName = "service", NodeRegistrationTtl = value },
            _ => throw new ArgumentOutOfRangeException(nameof(option), option, null),
        };
}

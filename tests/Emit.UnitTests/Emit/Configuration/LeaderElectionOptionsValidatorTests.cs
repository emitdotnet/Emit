namespace Emit.UnitTests.Configuration;

using global::Emit.Configuration;
using Xunit;

public class LeaderElectionOptionsValidatorTests
{
    private readonly LeaderElectionOptionsValidator validator = new();

    [Fact]
    public void GivenDefaultOptions_WhenValidated_ThenSucceeds()
    {
        // Arrange
        var options = new LeaderElectionOptions();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GivenZeroOrNegativeQueryTimeout_WhenValidated_ThenFails(int seconds)
    {
        // Arrange
        var options = new LeaderElectionOptions { QueryTimeout = TimeSpan.FromSeconds(seconds) };

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains(nameof(LeaderElectionOptions.QueryTimeout), result.FailureMessage);
    }

    [Fact]
    public void GivenHeartbeatIntervalNotGreaterThanQueryTimeout_WhenValidated_ThenFails()
    {
        // Arrange
        var options = new LeaderElectionOptions
        {
            QueryTimeout = TimeSpan.FromSeconds(10),
            HeartbeatInterval = TimeSpan.FromSeconds(10) // equal, not greater
        };

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains(nameof(LeaderElectionOptions.HeartbeatInterval), result.FailureMessage);
        Assert.Contains(nameof(LeaderElectionOptions.QueryTimeout), result.FailureMessage);
    }

    [Fact]
    public void GivenLeaseDurationNotGreaterThanHeartbeatPlusQueryTimeout_WhenValidated_ThenFails()
    {
        // Arrange
        var options = new LeaderElectionOptions
        {
            QueryTimeout = TimeSpan.FromSeconds(5),
            HeartbeatInterval = TimeSpan.FromSeconds(15),
            LeaseDuration = TimeSpan.FromSeconds(20) // equal to HI+QT, not greater
        };

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains(nameof(LeaderElectionOptions.LeaseDuration), result.FailureMessage);
    }

    [Fact]
    public void GivenNodeRegistrationTtlNotGreaterThanLeaseDuration_WhenValidated_ThenFails()
    {
        // Arrange
        var options = new LeaderElectionOptions
        {
            LeaseDuration = TimeSpan.FromSeconds(60),
            NodeRegistrationTtl = TimeSpan.FromSeconds(60) // equal, not greater
        };

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains(nameof(LeaderElectionOptions.NodeRegistrationTtl), result.FailureMessage);
        Assert.Contains(nameof(LeaderElectionOptions.LeaseDuration), result.FailureMessage);
    }

    [Fact]
    public void GivenMultipleViolations_WhenValidated_ThenReportsAll()
    {
        // Arrange
        var options = new LeaderElectionOptions
        {
            QueryTimeout = TimeSpan.Zero,
            HeartbeatInterval = TimeSpan.Zero,
            LeaseDuration = TimeSpan.Zero,
            NodeRegistrationTtl = TimeSpan.Zero
        };

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains(nameof(LeaderElectionOptions.QueryTimeout), result.FailureMessage);
    }

    [Fact]
    public void GivenValidCustomOptions_WhenValidated_ThenSucceeds()
    {
        // Arrange — QT(3s) < HI(10s) < HI+QT(13s) < LD(30s) < TTL(45s)
        var options = new LeaderElectionOptions
        {
            QueryTimeout = TimeSpan.FromSeconds(3),
            HeartbeatInterval = TimeSpan.FromSeconds(10),
            LeaseDuration = TimeSpan.FromSeconds(30),
            NodeRegistrationTtl = TimeSpan.FromSeconds(45)
        };

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }
}

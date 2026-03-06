namespace Emit.UnitTests.Configuration;

using global::Emit.Configuration;
using Xunit;

public class DaemonOptionsValidatorTests
{
    private readonly DaemonOptionsValidator validator = new();

    [Fact]
    public void GivenDefaultOptions_WhenValidated_ThenSucceeds()
    {
        // Arrange
        var options = new DaemonOptions();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void GivenZeroAcknowledgeTimeout_WhenValidated_ThenFails()
    {
        // Arrange
        var options = new DaemonOptions { AcknowledgeTimeout = TimeSpan.Zero };

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains($"{nameof(DaemonOptions.AcknowledgeTimeout)} must be greater than zero.", result.FailureMessage);
    }

    [Fact]
    public void GivenNegativeAcknowledgeTimeout_WhenValidated_ThenFails()
    {
        // Arrange
        var options = new DaemonOptions { AcknowledgeTimeout = TimeSpan.FromSeconds(-1) };

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains($"{nameof(DaemonOptions.AcknowledgeTimeout)} must be greater than zero.", result.FailureMessage);
    }

    [Fact]
    public void GivenZeroDrainTimeout_WhenValidated_ThenFails()
    {
        // Arrange
        var options = new DaemonOptions { DrainTimeout = TimeSpan.Zero };

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains($"{nameof(DaemonOptions.DrainTimeout)} must be greater than zero.", result.FailureMessage);
    }

    [Fact]
    public void GivenNegativeDrainTimeout_WhenValidated_ThenFails()
    {
        // Arrange
        var options = new DaemonOptions { DrainTimeout = TimeSpan.FromSeconds(-1) };

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains($"{nameof(DaemonOptions.DrainTimeout)} must be greater than zero.", result.FailureMessage);
    }

    [Fact]
    public void GivenMultipleViolations_WhenValidated_ThenReportsAll()
    {
        // Arrange
        var options = new DaemonOptions
        {
            AcknowledgeTimeout = TimeSpan.Zero,
            DrainTimeout = TimeSpan.Zero
        };

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains($"{nameof(DaemonOptions.AcknowledgeTimeout)} must be greater than zero.", result.FailureMessage);
        Assert.Contains($"{nameof(DaemonOptions.DrainTimeout)} must be greater than zero.", result.FailureMessage);
    }
}

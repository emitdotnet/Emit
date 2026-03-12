namespace Emit.UnitTests.Configuration;

using global::Emit.Configuration;
using Xunit;

public class OutboxOptionsValidatorTests
{
    private readonly OutboxOptionsValidator validator = new();

    [Fact]
    public void GivenDefaultOptions_WhenValidated_ThenSucceeds()
    {
        // Arrange
        var options = new OutboxOptions();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GivenZeroOrNegativeBatchSize_WhenValidated_ThenFails(int batchSize)
    {
        // Arrange
        var options = new OutboxOptions { BatchSize = batchSize };

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains($"{nameof(OutboxOptions.BatchSize)} must be greater than 0", result.FailureMessage);
    }

    [Fact]
    public void GivenBatchSizeExceedsMax_WhenValidated_ThenFails()
    {
        // Arrange
        var options = new OutboxOptions { BatchSize = ValidationConstants.MaxBatchSize + 1 };

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains($"{nameof(OutboxOptions.BatchSize)} must be at most {ValidationConstants.MaxBatchSize}", result.FailureMessage);
    }

    [Fact]
    public void GivenPollingIntervalBelowMinimum_WhenValidated_ThenFails()
    {
        // Arrange
        var options = new OutboxOptions
        {
            PollingInterval = ValidationConstants.MinPollingInterval - TimeSpan.FromMilliseconds(1)
        };

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains($"{nameof(OutboxOptions.PollingInterval)} must be at least", result.FailureMessage);
    }

    [Fact]
    public void GivenMultipleViolations_WhenValidated_ThenReportsAll()
    {
        // Arrange
        var options = new OutboxOptions
        {
            PollingInterval = TimeSpan.Zero,
            BatchSize = 0
        };

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains($"{nameof(OutboxOptions.PollingInterval)} must be at least", result.FailureMessage);
        Assert.Contains($"{nameof(OutboxOptions.BatchSize)} must be greater than 0", result.FailureMessage);
    }
}

namespace Emit.Tests.Configuration;

using Emit.Configuration;
using Xunit;

public class CleanupOptionsValidatorTests
{
    private readonly CleanupOptionsValidator validator = new();

    [Fact]
    public void GivenDefaultOptions_WhenValidate_ThenSucceeds()
    {
        // Arrange
        var options = new CleanupOptions();

        // Act
        var result = validator.Validate(options);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void GivenRetentionPeriodBelowMinimum_WhenValidate_ThenFails()
    {
        // Arrange
        var options = new CleanupOptions
        {
            RetentionPeriod = TimeSpan.FromMinutes(30)
        };

        // Act
        var result = validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CleanupOptions.RetentionPeriod));
    }

    [Fact]
    public void GivenRetentionPeriodAtMinimum_WhenValidate_ThenSucceeds()
    {
        // Arrange
        var options = new CleanupOptions
        {
            RetentionPeriod = CleanupOptionsValidator.MinRetentionPeriod
        };

        // Act
        var result = validator.Validate(options);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void GivenCleanupIntervalBelowMinimum_WhenValidate_ThenFails()
    {
        // Arrange
        var options = new CleanupOptions
        {
            CleanupInterval = TimeSpan.FromMinutes(1)
        };

        // Act
        var result = validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CleanupOptions.CleanupInterval));
    }

    [Fact]
    public void GivenCleanupIntervalAtMinimum_WhenValidate_ThenSucceeds()
    {
        // Arrange
        var options = new CleanupOptions
        {
            CleanupInterval = CleanupOptionsValidator.MinCleanupInterval
        };

        // Act
        var result = validator.Validate(options);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void GivenBatchSizeZero_WhenValidate_ThenFails()
    {
        // Arrange
        var options = new CleanupOptions
        {
            BatchSize = 0
        };

        // Act
        var result = validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CleanupOptions.BatchSize));
    }

    [Fact]
    public void GivenBatchSizeAboveMaximum_WhenValidate_ThenFails()
    {
        // Arrange
        var options = new CleanupOptions
        {
            BatchSize = CleanupOptionsValidator.MaxBatchSize + 1
        };

        // Act
        var result = validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CleanupOptions.BatchSize));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public void GivenBatchSizeInValidRange_WhenValidate_ThenSucceeds(int batchSize)
    {
        // Arrange
        var options = new CleanupOptions
        {
            BatchSize = batchSize
        };

        // Act
        var result = validator.Validate(options);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void GivenAllInvalidOptions_WhenValidate_ThenFailsWithMultipleErrors()
    {
        // Arrange
        var options = new CleanupOptions
        {
            RetentionPeriod = TimeSpan.FromMinutes(1),
            CleanupInterval = TimeSpan.FromSeconds(10),
            BatchSize = -1
        };

        // Act
        var result = validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Count);
    }
}

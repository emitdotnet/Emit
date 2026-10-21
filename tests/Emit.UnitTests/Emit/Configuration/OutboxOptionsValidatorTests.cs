namespace Emit.UnitTests.Configuration;

using global::Emit.Configuration;
using Xunit;

public class OutboxOptionsValidatorTests
{
    private readonly OutboxOptionsValidator validator = new();

    public enum Bound
    {
        LeastBatchSize,
        LargestBatchSize,
        LeastConcurrentGroups,
        ShortestPollingInterval,
    }

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
    [InlineData(Bound.LeastBatchSize, true)]
    [InlineData(Bound.LeastBatchSize, false)]
    [InlineData(Bound.LargestBatchSize, true)]
    [InlineData(Bound.LargestBatchSize, false)]
    [InlineData(Bound.LeastConcurrentGroups, true)]
    [InlineData(Bound.LeastConcurrentGroups, false)]
    [InlineData(Bound.ShortestPollingInterval, true)]
    [InlineData(Bound.ShortestPollingInterval, false)]
    public void GivenOptionAtOrJustPastItsBound_WhenValidated_ThenOnlyTheBoundItselfIsAccepted(Bound bound, bool atBound)
    {
        // Arrange
        var (option, options) = bound switch
        {
            Bound.LeastBatchSize => (nameof(OutboxOptions.BatchSize), new OutboxOptions { BatchSize = atBound ? 1 : 0 }),
            Bound.LargestBatchSize => (nameof(OutboxOptions.BatchSize), new OutboxOptions { BatchSize = ValidationConstants.MaxBatchSize + (atBound ? 0 : 1) }),
            Bound.LeastConcurrentGroups => (nameof(OutboxOptions.MaxConcurrentGroups), new OutboxOptions { MaxConcurrentGroups = atBound ? 1 : 0 }),
            _ => (nameof(OutboxOptions.PollingInterval), new OutboxOptions
            {
                PollingInterval = ValidationConstants.MinPollingInterval - (atBound ? TimeSpan.Zero : TimeSpan.FromMilliseconds(1)),
            }),
        };

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.Equal(atBound, result.Succeeded);
        if (!atBound)
        {
            Assert.All(result.Failures!, f => Assert.StartsWith($"{option} ", f, StringComparison.Ordinal));
        }
    }
}

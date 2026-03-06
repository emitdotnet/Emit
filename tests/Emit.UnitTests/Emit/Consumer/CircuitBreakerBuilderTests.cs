namespace Emit.UnitTests.Consumer;

using global::Emit.Consumer;
using Xunit;

public sealed class CircuitBreakerBuilderTests
{
    [Fact]
    public void GivenValidConfiguration_WhenBuild_ThenReturnsConfig()
    {
        // Arrange
        var builder = new CircuitBreakerBuilder()
            .FailureThreshold(5)
            .SamplingWindow(TimeSpan.FromSeconds(30))
            .PauseDuration(TimeSpan.FromSeconds(10));

        // Act
        var config = builder.Build();

        // Assert
        Assert.Equal(5, config.FailureThreshold);
        Assert.Equal(TimeSpan.FromSeconds(30), config.SamplingWindow);
        Assert.Equal(TimeSpan.FromSeconds(10), config.PauseDuration);
        Assert.Null(config.TripOnExceptionTypes);
    }

    [Fact]
    public void GivenTripOnConfigured_WhenBuild_ThenConfigContainsExceptionTypes()
    {
        // Arrange
        var builder = new CircuitBreakerBuilder()
            .FailureThreshold(5)
            .SamplingWindow(TimeSpan.FromSeconds(30))
            .PauseDuration(TimeSpan.FromSeconds(10))
            .TripOn<InvalidOperationException>()
            .TripOn<TimeoutException>();

        // Act
        var config = builder.Build();

        // Assert
        Assert.NotNull(config.TripOnExceptionTypes);
        Assert.Equal(2, config.TripOnExceptionTypes.Length);
        Assert.Contains(typeof(InvalidOperationException), config.TripOnExceptionTypes);
        Assert.Contains(typeof(TimeoutException), config.TripOnExceptionTypes);
    }

    [Fact]
    public void GivenFailureThresholdZero_WhenSetting_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = new CircuitBreakerBuilder();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.FailureThreshold(0));
    }

    [Fact]
    public void GivenFailureThresholdNegative_WhenSetting_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = new CircuitBreakerBuilder();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.FailureThreshold(-1));
    }

    [Fact]
    public void GivenSamplingWindowZero_WhenSetting_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = new CircuitBreakerBuilder();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.SamplingWindow(TimeSpan.Zero));
    }

    [Fact]
    public void GivenSamplingWindowNegative_WhenSetting_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = new CircuitBreakerBuilder();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.SamplingWindow(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void GivenPauseDurationZero_WhenSetting_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = new CircuitBreakerBuilder();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.PauseDuration(TimeSpan.Zero));
    }

    [Fact]
    public void GivenPauseDurationNegative_WhenSetting_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = new CircuitBreakerBuilder();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.PauseDuration(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void GivenMissingFailureThreshold_WhenBuild_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new CircuitBreakerBuilder()
            .SamplingWindow(TimeSpan.FromSeconds(30))
            .PauseDuration(TimeSpan.FromSeconds(10));

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains(nameof(CircuitBreakerBuilder.FailureThreshold), ex.Message);
    }

    [Fact]
    public void GivenMissingSamplingWindow_WhenBuild_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new CircuitBreakerBuilder()
            .FailureThreshold(5)
            .PauseDuration(TimeSpan.FromSeconds(10));

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains(nameof(CircuitBreakerBuilder.SamplingWindow), ex.Message);
    }

    [Fact]
    public void GivenMissingPauseDuration_WhenBuild_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new CircuitBreakerBuilder()
            .FailureThreshold(5)
            .SamplingWindow(TimeSpan.FromSeconds(30));

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains(nameof(CircuitBreakerBuilder.PauseDuration), ex.Message);
    }

    [Fact]
    public void GivenChaining_WhenCallingMethods_ThenReturnsSameBuilder()
    {
        // Arrange
        var builder = new CircuitBreakerBuilder();

        // Act
        var result = builder
            .FailureThreshold(5)
            .SamplingWindow(TimeSpan.FromSeconds(30))
            .PauseDuration(TimeSpan.FromSeconds(10))
            .TripOn<InvalidOperationException>();

        // Assert
        Assert.Same(builder, result);
    }
}

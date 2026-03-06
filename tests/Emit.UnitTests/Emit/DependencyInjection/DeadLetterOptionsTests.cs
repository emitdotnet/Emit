namespace Emit.UnitTests.DependencyInjection;

using global::Emit.DependencyInjection;
using Xunit;

public sealed class DeadLetterOptionsTests
{
    [Theory]
    [InlineData("orders", "orders.dlt")]
    [InlineData("user-events", "user-events.dlt")]
    [InlineData("payments.v2", "payments.v2.dlt")]
    public void GivenDefaultConvention_WhenCalled_ThenAppendsDlt(string sourceTopic, string expected)
    {
        // Arrange
        var options = new DeadLetterOptions();

        // Act
        var result = options.TopicNamingConvention(sourceTopic);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GivenCustomConvention_WhenSet_ThenUsesCustomConvention()
    {
        // Arrange
        var options = new DeadLetterOptions
        {
            TopicNamingConvention = source => $"dlq.{source}",
        };

        // Act
        var result = options.TopicNamingConvention("orders");

        // Assert
        Assert.Equal("dlq.orders", result);
    }
}

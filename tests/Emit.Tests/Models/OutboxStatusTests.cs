namespace Emit.Tests.Models;

using Emit.Models;
using Xunit;

public class OutboxStatusTests
{
    [Fact]
    public void GivenOutboxStatus_WhenCheckingValues_ThenHasExpectedMembers()
    {
        // Arrange & Act
        var values = Enum.GetValues<OutboxStatus>();

        // Assert
        Assert.Equal(3, values.Length);
        Assert.Contains(OutboxStatus.Pending, values);
        Assert.Contains(OutboxStatus.Completed, values);
        Assert.Contains(OutboxStatus.Failed, values);
    }

    [Fact]
    public void GivenOutboxStatus_WhenCheckingUnderlyingValues_ThenHasExpectedIntValues()
    {
        // Arrange & Act & Assert
        Assert.Equal(0, (int)OutboxStatus.Pending);
        Assert.Equal(1, (int)OutboxStatus.Completed);
        Assert.Equal(2, (int)OutboxStatus.Failed);
    }

    [Fact]
    public void GivenOutboxStatus_WhenDefaultValue_ThenIsPending()
    {
        // Arrange & Act
        var defaultStatus = default(OutboxStatus);

        // Assert
        Assert.Equal(OutboxStatus.Pending, defaultStatus);
    }
}

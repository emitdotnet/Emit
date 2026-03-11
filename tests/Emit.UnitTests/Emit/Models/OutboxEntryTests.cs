namespace Emit.UnitTests.Models;

using global::Emit.Models;
using Xunit;

public class OutboxEntryTests
{
    [Fact]
    public void GivenRequiredProperties_WhenCreatingEntry_ThenDefaultValuesAreSet()
    {
        // Arrange & Act
        var entry = new OutboxEntry
        {
            SystemId = "kafka",
            Destination = "kafka://localhost:9092/test-topic",
            GroupKey = "cluster:topic",
            Body = [1, 2, 3]
        };

        // Assert
        Assert.Null(entry.Id);
        Assert.Equal("kafka", entry.SystemId);
        Assert.Equal("kafka://localhost:9092/test-topic", entry.Destination);
        Assert.Equal("cluster:topic", entry.GroupKey);
        Assert.Equal(0L, entry.Sequence);
        Assert.Equal(default, entry.EnqueuedAt);
        Assert.Equal([1, 2, 3], entry.Body);
        Assert.Empty(entry.Properties);
        Assert.Empty(entry.Headers);
        Assert.Null(entry.ConversationId);
    }

    [Fact]
    public void GivenProperties_WhenSettingValues_ThenCanRetrieve()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act
        entry.Properties["topic"] = "orders";
        entry.Properties["cluster"] = "production";
        entry.Properties["valueType"] = "OrderCreated";

        // Assert
        Assert.Equal(3, entry.Properties.Count);
        Assert.Equal("orders", entry.Properties["topic"]);
        Assert.Equal("production", entry.Properties["cluster"]);
        Assert.Equal("OrderCreated", entry.Properties["valueType"]);
    }

    private static OutboxEntry CreateTestEntry() => new()
    {
        SystemId = "kafka",
        Destination = "kafka://localhost:9092/test-topic",
        GroupKey = "cluster:topic",
        Body = [1, 2, 3]
    };
}

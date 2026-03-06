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
            ProviderId = "kafka",
            RegistrationKey = "__default__",
            GroupKey = "cluster:topic",
            Payload = [1, 2, 3]
        };

        // Assert
        Assert.Null(entry.Id);
        Assert.Equal("kafka", entry.ProviderId);
        Assert.Equal("__default__", entry.RegistrationKey);
        Assert.Equal("cluster:topic", entry.GroupKey);
        Assert.Equal(0L, entry.Sequence);
        Assert.Equal(default, entry.EnqueuedAt);
        Assert.Equal([1, 2, 3], entry.Payload);
        Assert.Empty(entry.Properties);
        Assert.Null(entry.TraceParent);
        Assert.Null(entry.TraceState);
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
        ProviderId = "kafka",
        RegistrationKey = "__default__",
        GroupKey = "cluster:topic",
        Payload = [1, 2, 3]
    };
}

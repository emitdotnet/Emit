namespace Emit.Kafka.Tests.Consumer;

using global::Emit.Consumer;
using Xunit;

public sealed class DeadLetterTopicMapTests
{
    [Fact]
    public void GivenGroupLevelEntry_WhenResolveWithNullConsumerKey_ThenReturnsDlqTopic()
    {
        // Arrange
        var map = new DeadLetterTopicMap(
        [
            new DeadLetterEntry { ConsumerKey = null, SourceTopic = "orders", DeadLetterTopic = "orders.dlt" },
        ]);

        // Act
        var result = map.Resolve(null, "orders");

        // Assert
        Assert.Equal("orders.dlt", result);
    }

    [Fact]
    public void GivenExplicitTopicOverride_WhenResolve_ThenReturnsExplicitTopic()
    {
        // Arrange
        var map = new DeadLetterTopicMap(
        [
            new DeadLetterEntry { ConsumerKey = "MyConsumer", SourceTopic = "orders", DeadLetterTopic = "custom-dlq" },
        ]);

        // Act
        var result = map.Resolve("MyConsumer", "orders");

        // Assert
        Assert.Equal("custom-dlq", result);
    }

    [Fact]
    public void GivenConsumerLevelEntry_WhenResolveWithConsumerKey_ThenReturnsConsumerTopic()
    {
        // Arrange
        var map = new DeadLetterTopicMap(
        [
            new DeadLetterEntry { ConsumerKey = null, SourceTopic = "orders", DeadLetterTopic = "orders.dlt" },
            new DeadLetterEntry { ConsumerKey = "AuditConsumer", SourceTopic = "orders", DeadLetterTopic = "audit-dlq" },
        ]);

        // Act
        var result = map.Resolve("AuditConsumer", "orders");

        // Assert
        Assert.Equal("audit-dlq", result);
    }

    [Fact]
    public void GivenNoConsumerLevelEntry_WhenResolveWithConsumerKey_ThenFallsBackToGroupLevel()
    {
        // Arrange
        var map = new DeadLetterTopicMap(
        [
            new DeadLetterEntry { ConsumerKey = null, SourceTopic = "orders", DeadLetterTopic = "orders.dlt" },
        ]);

        // Act
        var result = map.Resolve("UnknownConsumer", "orders");

        // Assert
        Assert.Equal("orders.dlt", result);
    }

    [Fact]
    public void GivenNoMatchingEntry_WhenResolve_ThenReturnsNull()
    {
        // Arrange
        var map = new DeadLetterTopicMap(
        [
            new DeadLetterEntry { ConsumerKey = null, SourceTopic = "orders", DeadLetterTopic = "orders.dlt" },
        ]);

        // Act
        var result = map.Resolve(null, "payments");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GivenMultipleDlqTopics_WhenAllTopics_ThenContainsUniqueTopics()
    {
        // Arrange
        var map = new DeadLetterTopicMap(
        [
            new DeadLetterEntry { ConsumerKey = null, SourceTopic = "orders", DeadLetterTopic = "orders.dlt" },
            new DeadLetterEntry { ConsumerKey = null, SourceTopic = "payments", DeadLetterTopic = "payments.dlt" },
            new DeadLetterEntry { ConsumerKey = "AuditConsumer", SourceTopic = "orders", DeadLetterTopic = "audit-dlq" },
        ]);

        // Act & Assert
        Assert.Equal(3, map.AllTopics.Count);
        Assert.Contains("orders.dlt", map.AllTopics);
        Assert.Contains("payments.dlt", map.AllTopics);
        Assert.Contains("audit-dlq", map.AllTopics);
    }

    [Fact]
    public void GivenMultipleConsumersSameDlq_WhenAllTopics_ThenDeduplicates()
    {
        // Arrange
        var map = new DeadLetterTopicMap(
        [
            new DeadLetterEntry { ConsumerKey = "ConsumerA", SourceTopic = "orders", DeadLetterTopic = "orders.dlt" },
            new DeadLetterEntry { ConsumerKey = "ConsumerB", SourceTopic = "orders", DeadLetterTopic = "orders.dlt" },
            new DeadLetterEntry { ConsumerKey = null, SourceTopic = "orders", DeadLetterTopic = "orders.dlt" },
        ]);

        // Act & Assert
        Assert.Single(map.AllTopics);
        Assert.Contains("orders.dlt", map.AllTopics);
    }

    [Fact]
    public void GivenEmptyMap_WhenResolve_ThenReturnsNull()
    {
        // Arrange
        var map = DeadLetterTopicMap.Empty;

        // Act
        var result = map.Resolve("AnyConsumer", "any-topic");

        // Assert
        Assert.Null(result);
    }
}

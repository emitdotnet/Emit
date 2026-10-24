namespace Emit.Kafka.Tests.Consumer;

using global::Emit.Kafka.Consumer;
using Xunit;

public sealed class KafkaTransportContextTests
{
    [Fact]
    public void GivenConsumedTombstoneWithAStringKey_WhenGettingTracingTags_ThenTheyAreWhereItWasConsumedFromItsKeyAndTombstone()
    {
        // Arrange
        var context = Create("order-1", partition: 2, offset: 7, groupId: "orders-group", isTombstone: true);

        // Act
        var tags = context.GetTracingTags().ToList();

        // Assert
        Assert.Equal(
            [
                new("messaging.destination.partition.id", "2"),
                new("messaging.kafka.offset", 7L),
                new("messaging.consumer.group.name", "orders-group"),
                new("messaging.kafka.message.tombstone", true),
                new("messaging.kafka.message.key", "order-1"),
            ],
            tags);
    }

    [Fact]
    public void GivenBatchWithANonStringKey_WhenGettingTracingTags_ThenOnlyTheConsumerGroupIsReported()
    {
        // Arrange: a batch has no partition or offset of its own, and a key that is not a string has no canonical
        // string form to report
        var context = Create(42, partition: -1, offset: -1, groupId: "orders-group", isTombstone: false);

        // Act
        var tags = context.GetTracingTags();

        // Assert
        Assert.Equal([new("messaging.consumer.group.name", "orders-group")], tags);
    }

    private static KafkaTransportContext<TKey> Create<TKey>(TKey key, int partition, long offset, string groupId, bool isTombstone) => new()
    {
        Key = key,
        MessageId = "message-1",
        Timestamp = DateTimeOffset.UtcNow,
        CancellationToken = CancellationToken.None,
        Services = null!,
        RawKey = null,
        RawValue = null,
        Headers = [],
        ProviderId = "kafka",
        Topic = "orders",
        Partition = partition,
        Offset = offset,
        GroupId = groupId,
        IsTombstone = isTombstone,
    };
}

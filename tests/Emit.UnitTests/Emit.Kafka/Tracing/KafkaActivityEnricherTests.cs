namespace Emit.Kafka.Tests.Tracing;

using System.Diagnostics;
using global::Emit.Abstractions;
using global::Emit.Abstractions.Tracing;
using global::Emit.Kafka.Tracing;
using Xunit;

public sealed class KafkaActivityEnricherTests
{
    private readonly KafkaActivityEnricher enricher = new();

    [Fact]
    public void GivenTombstoneWithAStringKey_WhenEnrichingItsCreateSpan_ThenItsKeyAndTombstoneAreReported()
    {
        // Arrange
        using var activity = new Activity("create");

        // Act
        enricher.Enrich(activity, Context(EnrichmentPhases.Produce, new KafkaProducePayload("order-1", IsTombstone: true)));

        // Assert
        Assert.Equal(
            [
                new("messaging.kafka.message.key", "order-1"),
                new("messaging.kafka.message.tombstone", true),
            ],
            activity.TagObjects);
    }

    [Fact]
    public void GivenMessageWithANonStringKey_WhenEnrichingItsCreateSpan_ThenNothingIsReported()
    {
        // Arrange: a key that is not a string has no canonical string form to report
        using var activity = new Activity("create");

        // Act
        enricher.Enrich(activity, Context(EnrichmentPhases.Produce, new KafkaProducePayload(42, IsTombstone: false)));

        // Assert
        Assert.Empty(activity.TagObjects);
    }

    [Fact]
    public void GivenAnotherPhase_WhenEnriching_ThenNothingIsReported()
    {
        // Arrange: a consumed message's attributes come from its transport context
        using var activity = new Activity("process");

        // Act
        enricher.Enrich(activity, Context(EnrichmentPhases.Consume, new KafkaProducePayload("order-1", IsTombstone: true)));

        // Assert
        Assert.Empty(activity.TagObjects);
    }

    private static EnrichmentContext Context(string phase, KafkaProducePayload payload)
    {
        var message = new SendContext<string>
        {
            MessageId = "message-1",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = null!,
            Message = "value",
        };
        message.SetPayload(payload);
        return new EnrichmentContext { MessageContext = message, Phase = phase };
    }
}

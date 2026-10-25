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
        enricher.Enrich(activity, Context(EnrichmentPhases.Produce, "order-1", message: null));

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
        enricher.Enrich(activity, Context(EnrichmentPhases.Produce, 42, message: "value"));

        // Assert
        Assert.Empty(activity.TagObjects);
    }

    [Fact]
    public void GivenAnotherPhase_WhenEnriching_ThenNothingIsReported()
    {
        // Arrange: a consumed message's attributes come from its transport context
        using var activity = new Activity("process");

        // Act
        enricher.Enrich(activity, Context(EnrichmentPhases.Consume, "order-1", message: null));

        // Assert
        Assert.Empty(activity.TagObjects);
    }

    private static EnrichmentContext Context<TKey>(string phase, TKey key, string? message)
    {
        var context = new SendContext<string?>
        {
            MessageId = "message-1",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = null!,
            Message = message,
        };
        context.SetPayload(new KafkaMessageKey<TKey>(key));
        return new EnrichmentContext { MessageContext = context, Phase = phase };
    }
}

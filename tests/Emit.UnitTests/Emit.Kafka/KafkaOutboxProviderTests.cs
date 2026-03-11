namespace Emit.Kafka.Tests;

using global::Emit.Abstractions.Metrics;
using global::Emit.Kafka;
using global::Emit.Kafka.Metrics;
using global::Emit.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaOutboxProviderTests
{
    private readonly Mock<ILogger<KafkaOutboxProvider>> loggerMock;

    public KafkaOutboxProviderTests()
    {
        loggerMock = new Mock<ILogger<KafkaOutboxProvider>>();
    }

    private static OutboxEntry CreateValidEntry() =>
        new()
        {
            Id = Guid.NewGuid(),
            SystemId = Provider.Identifier,
            Destination = "kafka://localhost:9092/test-topic",
            GroupKey = $"{Provider.Identifier}:test-topic",
            Sequence = 1,
            Body = [4, 5, 6],
            Properties = new Dictionary<string, string>
            {
                ["key"] = Convert.ToBase64String([1, 2, 3]),
                ["topic"] = "test-topic"
            },
            Headers = []
        };

    private static KafkaMetrics CreateKafkaMetrics() => new(null, new EmitMetricsEnrichment());

    #region Constructor Tests

    [Fact]
    public void GivenNullProducer_WhenCreating_ThenThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new KafkaOutboxProvider(null!, CreateKafkaMetrics(), loggerMock.Object));
        Assert.Equal("producer", exception.ParamName);
    }

    [Fact]
    public void GivenNullLogger_WhenCreating_ThenThrowsArgumentNullException()
    {
        // Arrange
        var producerMock = new Mock<ConfluentKafka.IProducer<byte[], byte[]>>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new KafkaOutboxProvider(producerMock.Object, CreateKafkaMetrics(), null!));
        Assert.Equal("logger", exception.ParamName);
    }

    #endregion

    #region ProcessAsync Parameter Validation Tests

    [Fact]
    public async Task GivenNullEntry_WhenProcessAsync_ThenThrowsArgumentNullException()
    {
        // Arrange
        var producerMock = new Mock<ConfluentKafka.IProducer<byte[], byte[]>>();
        var provider = new KafkaOutboxProvider(producerMock.Object, CreateKafkaMetrics(), loggerMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            provider.ProcessAsync(null!));
    }

    #endregion

    #region ProcessAsync Destination Validation Tests

    [Fact]
    public async Task GivenInvalidDestination_WhenProcessAsync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var producerMock = new Mock<ConfluentKafka.IProducer<byte[], byte[]>>();
        var provider = new KafkaOutboxProvider(producerMock.Object, CreateKafkaMetrics(), loggerMock.Object);
        var entry = new OutboxEntry
        {
            Id = Guid.NewGuid(),
            SystemId = Provider.Identifier,
            Destination = "kafka://localhost:9092",
            GroupKey = "kafka:test-topic",
            Sequence = 1
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ProcessAsync(entry));
    }

    #endregion
}

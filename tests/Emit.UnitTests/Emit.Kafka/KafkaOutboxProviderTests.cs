namespace Emit.Kafka.Tests;

using global::Emit.Abstractions.Metrics;
using global::Emit.Models;
using global::Emit.Kafka;
using global::Emit.Kafka.Metrics;
using global::Emit.Kafka.Serialization;
using MessagePack;
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

    private static OutboxEntry CreateValidEntry()
    {
        var payload = new KafkaPayload
        {
            Topic = "test-topic",
            KeyBytes = [1, 2, 3],
            ValueBytes = [4, 5, 6]
        };

        return new OutboxEntry
        {
            Id = Guid.NewGuid(),
            ProviderId = Provider.Identifier,
            RegistrationKey = Provider.Identifier,
            GroupKey = $"{Provider.Identifier}:test-topic",
            Sequence = 1,
            Payload = MessagePackSerializer.Serialize(payload)
        };
    }

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

    #region ProcessAsync Payload Deserialization Tests

    [Fact]
    public async Task GivenInvalidPayload_WhenProcessAsync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var producerMock = new Mock<ConfluentKafka.IProducer<byte[], byte[]>>();
        var provider = new KafkaOutboxProvider(producerMock.Object, CreateKafkaMetrics(), loggerMock.Object);
        var entry = new OutboxEntry
        {
            Id = Guid.NewGuid(),
            ProviderId = Provider.Identifier,
            RegistrationKey = Provider.Identifier,
            GroupKey = "test-group",
            Sequence = 1,
            Payload = [0xFF, 0xFF, 0xFF]
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ProcessAsync(entry));
        Assert.NotNull(exception.Message);
        Assert.Contains("Failed to deserialize Kafka payload", exception.Message);
        Assert.Contains(entry.Id.ToString()!, exception.Message!);
    }

    #endregion
}

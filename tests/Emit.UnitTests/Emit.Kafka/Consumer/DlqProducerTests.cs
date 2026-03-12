namespace Emit.Kafka.Tests.Consumer;

using System.Text;
using global::Emit.Kafka.Consumer;
using global::Emit.Kafka.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public sealed class DlqProducerTests
{
    private readonly Mock<ConfluentKafka.IProducer<byte[], byte[]>> mockProducer = new();
    private readonly KafkaDeadLetterOptions options = new()
    {
        TopicName = "orders.dlt",
        DestinationAddress = new Uri("kafka://broker:9092/orders.dlt"),
    };
    private readonly DlqProducer sut;

    public DlqProducerTests()
    {
        sut = new DlqProducer(mockProducer.Object, options, NullLogger<DlqProducer>.Instance);
    }

    [Fact]
    public void GivenKafkaDeadLetterOptions_WhenConstructed_ThenDestinationAddressMatchesOptions()
    {
        // Assert
        Assert.Equal(options.DestinationAddress, sut.DestinationAddress);
    }

    [Fact]
    public async Task GivenRawBytes_WhenProduceAsync_ThenBytesPassedUnchanged()
    {
        // Arrange
        var rawKey = Encoding.UTF8.GetBytes("key-data");
        var rawValue = Encoding.UTF8.GetBytes("value-data");
        ConfluentKafka.Message<byte[], byte[]>? capturedMessage = null;
        mockProducer
            .Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<ConfluentKafka.Message<byte[], byte[]>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ConfluentKafka.Message<byte[], byte[]>, CancellationToken>(
                (_, msg, _) => capturedMessage = msg)
            .ReturnsAsync(new ConfluentKafka.DeliveryResult<byte[], byte[]>());

        // Act
        await sut.ProduceAsync(rawKey, rawValue, [], CancellationToken.None);

        // Assert
        Assert.NotNull(capturedMessage);
        Assert.Equal(rawKey, capturedMessage.Key);
        Assert.Equal(rawValue, capturedMessage.Value);
    }

    [Fact]
    public async Task GivenHeaders_WhenProduceAsync_ThenHeadersSetOnMessage()
    {
        // Arrange
        var headers = new List<KeyValuePair<string, string>>
        {
            new("x-emit-original-topic", "orders"),
            new("x-emit-partition", "0"),
            new("x-emit-offset", "42"),
        };
        ConfluentKafka.Message<byte[], byte[]>? capturedMessage = null;
        mockProducer
            .Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<ConfluentKafka.Message<byte[], byte[]>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ConfluentKafka.Message<byte[], byte[]>, CancellationToken>(
                (_, msg, _) => capturedMessage = msg)
            .ReturnsAsync(new ConfluentKafka.DeliveryResult<byte[], byte[]>());

        // Act
        await sut.ProduceAsync(null, null, headers, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedMessage);
        Assert.Equal(3, capturedMessage.Headers.Count);
        Assert.Equal("orders", Encoding.UTF8.GetString(capturedMessage.Headers[0].GetValueBytes()));
    }

    [Fact]
    public async Task GivenOriginalAndEmitHeaders_WhenProduceAsync_ThenAllHeadersPreserved()
    {
        // Arrange
        var headers = new List<KeyValuePair<string, string>>
        {
            new("correlation-id", "abc-123"),
            new("x-emit-original-topic", "orders"),
        };
        ConfluentKafka.Message<byte[], byte[]>? capturedMessage = null;
        mockProducer
            .Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<ConfluentKafka.Message<byte[], byte[]>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ConfluentKafka.Message<byte[], byte[]>, CancellationToken>(
                (_, msg, _) => capturedMessage = msg)
            .ReturnsAsync(new ConfluentKafka.DeliveryResult<byte[], byte[]>());

        // Act
        await sut.ProduceAsync(null, null, headers, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedMessage);
        Assert.Equal(2, capturedMessage.Headers.Count);
        var correlationHeader = capturedMessage.Headers[0];
        Assert.Equal("correlation-id", correlationHeader.Key);
        Assert.Equal("abc-123", Encoding.UTF8.GetString(correlationHeader.GetValueBytes()));
    }

    [Fact]
    public async Task GivenProduceAsync_WhenFirstAttemptSucceeds_ThenNoRetry()
    {
        // Arrange
        mockProducer
            .Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<ConfluentKafka.Message<byte[], byte[]>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfluentKafka.DeliveryResult<byte[], byte[]>());

        // Act
        await sut.ProduceAsync(null, null, [], CancellationToken.None);

        // Assert
        mockProducer.Verify(
            p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<ConfluentKafka.Message<byte[], byte[]>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GivenTransientFailure_WhenProduceAsync_ThenRetries()
    {
        // Arrange
        var callCount = 0;
        mockProducer
            .Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<ConfluentKafka.Message<byte[], byte[]>>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, ConfluentKafka.Message<byte[], byte[]>, CancellationToken>((_, _, _) =>
            {
                callCount++;
                if (callCount <= 2)
                {
                    throw new ConfluentKafka.ProduceException<byte[], byte[]>(
                        new ConfluentKafka.Error(ConfluentKafka.ErrorCode.BrokerNotAvailable),
                        new ConfluentKafka.DeliveryResult<byte[], byte[]>());
                }

                return Task.FromResult(new ConfluentKafka.DeliveryResult<byte[], byte[]>());
            });

        // Act
        await sut.ProduceAsync(null, null, [], CancellationToken.None);

        // Assert — 2 failures + 1 success = 3 calls
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task GivenAllAttemptsFail_WhenProduceAsync_ThenThrows()
    {
        // Arrange
        mockProducer
            .Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<ConfluentKafka.Message<byte[], byte[]>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConfluentKafka.ProduceException<byte[], byte[]>(
                new ConfluentKafka.Error(ConfluentKafka.ErrorCode.BrokerNotAvailable),
                new ConfluentKafka.DeliveryResult<byte[], byte[]>()));

        // Act & Assert
        await Assert.ThrowsAsync<ConfluentKafka.ProduceException<byte[], byte[]>>(
            () => sut.ProduceAsync(null, null, [], CancellationToken.None));
    }

    [Fact]
    public async Task GivenOptions_WhenProduceAsync_ThenProducesToConfiguredTopic()
    {
        // Arrange
        string? capturedTopic = null;
        mockProducer
            .Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<ConfluentKafka.Message<byte[], byte[]>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ConfluentKafka.Message<byte[], byte[]>, CancellationToken>(
                (topic, _, _) => capturedTopic = topic)
            .ReturnsAsync(new ConfluentKafka.DeliveryResult<byte[], byte[]>());

        // Act
        await sut.ProduceAsync(null, null, [], CancellationToken.None);

        // Assert
        Assert.Equal("orders.dlt", capturedTopic);
    }
}

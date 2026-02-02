namespace Emit.Tests.Provider.Kafka;

using Confluent.Kafka;
using Emit.Abstractions;
using Emit.Models;
using Emit.Provider.Kafka;
using Emit.Resilience;
using Microsoft.Extensions.Logging;
using Moq;
using Transactional.Abstractions;
using Xunit;

public class KafkaProducerTests
{
    private readonly Mock<IOutboxRepository> repositoryMock;
    private readonly Mock<ISerializer<string>> keySerializerMock;
    private readonly Mock<ISerializer<string>> valueSerializerMock;
    private readonly Mock<ILogger<KafkaProducer<string, string>>> loggerMock;
    private readonly string registrationKey = "test-registration";
    private readonly string clusterIdentifier = "localhost:9092";
    private readonly ResiliencePolicy resiliencePolicy = ResiliencePolicy.Default;

    public KafkaProducerTests()
    {
        repositoryMock = new Mock<IOutboxRepository>();
        keySerializerMock = new Mock<ISerializer<string>>();
        valueSerializerMock = new Mock<ISerializer<string>>();
        loggerMock = new Mock<ILogger<KafkaProducer<string, string>>>();

        repositoryMock
            .Setup(r => r.GetNextSequenceAsync(It.IsAny<string>(), It.IsAny<ITransactionContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);
    }

    private KafkaProducer<string, string> CreateProducer(
        ISerializer<string>? keySerializer = null,
        ISerializer<string>? valueSerializer = null)
    {
        return new KafkaProducer<string, string>(
            repositoryMock.Object,
            keySerializer ?? keySerializerMock.Object,
            valueSerializer ?? valueSerializerMock.Object,
            registrationKey,
            clusterIdentifier,
            resiliencePolicy,
            loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void GivenNullRepository_WhenCreating_ThenThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new KafkaProducer<string, string>(
                null!,
                keySerializerMock.Object,
                valueSerializerMock.Object,
                registrationKey,
                clusterIdentifier,
                resiliencePolicy,
                loggerMock.Object));
        Assert.Equal("repository", exception.ParamName);
    }

    [Fact]
    public void GivenNullRegistrationKey_WhenCreating_ThenThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new KafkaProducer<string, string>(
                repositoryMock.Object,
                keySerializerMock.Object,
                valueSerializerMock.Object,
                null!,
                clusterIdentifier,
                resiliencePolicy,
                loggerMock.Object));
        Assert.Equal("registrationKey", exception.ParamName);
    }

    [Fact]
    public void GivenNullClusterIdentifier_WhenCreating_ThenThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new KafkaProducer<string, string>(
                repositoryMock.Object,
                keySerializerMock.Object,
                valueSerializerMock.Object,
                registrationKey,
                null!,
                resiliencePolicy,
                loggerMock.Object));
        Assert.Equal("clusterIdentifier", exception.ParamName);
    }

    [Fact]
    public void GivenNullResiliencePolicy_WhenCreating_ThenThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new KafkaProducer<string, string>(
                repositoryMock.Object,
                keySerializerMock.Object,
                valueSerializerMock.Object,
                registrationKey,
                clusterIdentifier,
                null!,
                loggerMock.Object));
        Assert.Equal("resiliencePolicy", exception.ParamName);
    }

    [Fact]
    public void GivenNullLogger_WhenCreating_ThenThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new KafkaProducer<string, string>(
                repositoryMock.Object,
                keySerializerMock.Object,
                valueSerializerMock.Object,
                registrationKey,
                clusterIdentifier,
                resiliencePolicy,
                null!));
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void GivenNullSerializers_WhenCreating_ThenSucceeds()
    {
        // Arrange & Act (null serializers are allowed)
        var producer = new KafkaProducer<string, string>(
            repositoryMock.Object,
            null,
            null,
            registrationKey,
            clusterIdentifier,
            resiliencePolicy,
            loggerMock.Object);

        // Assert
        Assert.NotNull(producer);
    }

    #endregion

    #region ProduceAsync Parameter Validation Tests

    [Fact]
    public async Task GivenNullTopic_WhenProduceAsync_ThenThrowsArgumentNullException()
    {
        // Arrange
        var producer = CreateProducer();
        var message = new Message<string, string> { Key = "key", Value = "value" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            producer.ProduceAsync(null!, message));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GivenEmptyOrWhitespaceTopic_WhenProduceAsync_ThenThrowsArgumentException(string topic)
    {
        // Arrange
        var producer = CreateProducer();
        var message = new Message<string, string> { Key = "key", Value = "value" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            producer.ProduceAsync(topic, message));
    }

    [Fact]
    public async Task GivenNullMessage_WhenProduceAsync_ThenThrowsArgumentNullException()
    {
        // Arrange
        var producer = CreateProducer();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            producer.ProduceAsync("test-topic", null!));
    }

    #endregion

    #region ProduceAsync Serialization Tests

    [Fact]
    public async Task GivenNonNullKeyWithoutSerializer_WhenProduceAsync_ThenThrowsInvalidOperationException()
    {
        // Arrange - Create producer with null key serializer directly
        var producer = new KafkaProducer<string, string>(
            repositoryMock.Object,
            keySerializer: null,
            valueSerializerMock.Object,
            registrationKey,
            clusterIdentifier,
            resiliencePolicy,
            loggerMock.Object);
        var message = new Message<string, string> { Key = "key", Value = "value" };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            producer.ProduceAsync("test-topic", message));
        Assert.Contains("no serializer configured", exception.Message);
        Assert.Contains("key", exception.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task GivenNonNullValueWithoutSerializer_WhenProduceAsync_ThenThrowsInvalidOperationException()
    {
        // Arrange - Create producer with null value serializer directly
        keySerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([1, 2, 3]);
        var producer = new KafkaProducer<string, string>(
            repositoryMock.Object,
            keySerializerMock.Object,
            valueSerializer: null,
            registrationKey,
            clusterIdentifier,
            resiliencePolicy,
            loggerMock.Object);
        var message = new Message<string, string> { Key = "key", Value = "value" };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            producer.ProduceAsync("test-topic", message));
        Assert.Contains("no serializer configured", exception.Message);
        Assert.Contains("value", exception.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task GivenNullKey_WhenProduceAsync_ThenKeyBytesIsNull()
    {
        // Arrange - null key with any serializer should not call the serializer
        valueSerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([1, 2, 3]);

        var producer = CreateProducer();
        var message = new Message<string, string> { Key = null!, Value = "value" };

        OutboxEntry? capturedEntry = null;
        repositoryMock.Setup(r => r.EnqueueAsync(It.IsAny<OutboxEntry>(), It.IsAny<ITransactionContext?>(), It.IsAny<CancellationToken>()))
            .Callback<OutboxEntry, ITransactionContext?, CancellationToken>((e, _, _) => capturedEntry = e)
            .Returns(Task.CompletedTask);

        // Act
        await producer.ProduceAsync("test-topic", message);

        // Assert
        Assert.NotNull(capturedEntry);
        keySerializerMock.Verify(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()), Times.Never);
    }

    [Fact]
    public async Task GivenNullValue_WhenProduceAsync_ThenValueBytesIsNull()
    {
        // Arrange - null value with any serializer should not call the serializer
        keySerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([1, 2, 3]);

        var producer = CreateProducer();
        var message = new Message<string, string> { Key = "key", Value = null! };

        OutboxEntry? capturedEntry = null;
        repositoryMock.Setup(r => r.EnqueueAsync(It.IsAny<OutboxEntry>(), It.IsAny<ITransactionContext?>(), It.IsAny<CancellationToken>()))
            .Callback<OutboxEntry, ITransactionContext?, CancellationToken>((e, _, _) => capturedEntry = e)
            .Returns(Task.CompletedTask);

        // Act
        await producer.ProduceAsync("test-topic", message);

        // Assert
        Assert.NotNull(capturedEntry);
        valueSerializerMock.Verify(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()), Times.Never);
    }

    #endregion

    #region ProduceAsync Repository Interaction Tests

    [Fact]
    public async Task GivenValidMessage_WhenProduceAsync_ThenEnqueuesCorrectEntry()
    {
        // Arrange
        keySerializerMock.Setup(s => s.Serialize("test-key", It.IsAny<SerializationContext>()))
            .Returns([1, 2, 3]);
        valueSerializerMock.Setup(s => s.Serialize("test-value", It.IsAny<SerializationContext>()))
            .Returns([4, 5, 6]);

        var producer = CreateProducer();
        var message = new Message<string, string> { Key = "test-key", Value = "test-value" };

        OutboxEntry? capturedEntry = null;
        repositoryMock.Setup(r => r.EnqueueAsync(It.IsAny<OutboxEntry>(), It.IsAny<ITransactionContext?>(), It.IsAny<CancellationToken>()))
            .Callback<OutboxEntry, ITransactionContext?, CancellationToken>((e, _, _) => capturedEntry = e)
            .Returns(Task.CompletedTask);

        // Act
        await producer.ProduceAsync("test-topic", message);

        // Assert
        Assert.NotNull(capturedEntry);
        Assert.Equal(EmitConstants.Providers.Kafka, capturedEntry.ProviderId);
        Assert.Equal(registrationKey, capturedEntry.RegistrationKey);
        Assert.Equal($"{clusterIdentifier}:test-topic", capturedEntry.GroupKey);
        Assert.NotEmpty(capturedEntry.Payload);
        Assert.Equal("test-topic", capturedEntry.Properties["topic"]);
        Assert.Equal(clusterIdentifier, capturedEntry.Properties["cluster"]);
    }

    [Fact]
    public async Task GivenTransaction_WhenProduceAsync_ThenPassesTransactionToRepository()
    {
        // Arrange
        keySerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([1, 2, 3]);
        valueSerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([4, 5, 6]);

        var transactionMock = new Mock<ITransactionContext>();
        var producer = CreateProducer();
        var message = new Message<string, string> { Key = "key", Value = "value" };

        // Act
        await producer.ProduceAsync("test-topic", message, transactionMock.Object);

        // Assert
        repositoryMock.Verify(r => r.GetNextSequenceAsync(
            It.IsAny<string>(),
            transactionMock.Object,
            It.IsAny<CancellationToken>()), Times.Once);
        repositoryMock.Verify(r => r.EnqueueAsync(
            It.IsAny<OutboxEntry>(),
            transactionMock.Object,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenNoTransaction_WhenProduceAsync_ThenPassesNullTransactionToRepository()
    {
        // Arrange
        keySerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([1, 2, 3]);
        valueSerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([4, 5, 6]);

        var producer = CreateProducer();
        var message = new Message<string, string> { Key = "key", Value = "value" };

        // Act
        await producer.ProduceAsync("test-topic", message);

        // Assert
        repositoryMock.Verify(r => r.GetNextSequenceAsync(
            It.IsAny<string>(),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
        repositoryMock.Verify(r => r.EnqueueAsync(
            It.IsAny<OutboxEntry>(),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ProduceAsync DeliveryResult Tests

    [Fact]
    public async Task GivenValidMessage_WhenProduceAsync_ThenReturnsCorrectDeliveryResult()
    {
        // Arrange
        keySerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([1, 2, 3]);
        valueSerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([4, 5, 6]);

        repositoryMock.Setup(r => r.GetNextSequenceAsync(It.IsAny<string>(), It.IsAny<ITransactionContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(42L);

        var producer = CreateProducer();
        var message = new Message<string, string> { Key = "key", Value = "value" };

        // Act
        var result = await producer.ProduceAsync("test-topic", message);

        // Assert
        Assert.Equal("test-topic", result.Topic);
        Assert.Equal(new Offset(42L), result.Offset);
        Assert.Equal(PersistenceStatus.Persisted, result.Status);
        Assert.Same(message, result.Message);
    }

    #endregion

    #region Produce (Sync) Tests

    [Fact]
    public void GivenValidMessage_WhenProduce_ThenEnqueuesMessage()
    {
        // Arrange
        keySerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([1, 2, 3]);
        valueSerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([4, 5, 6]);

        var producer = CreateProducer();
        var message = new Message<string, string> { Key = "key", Value = "value" };

        // Act
        producer.Produce("test-topic", message);

        // Assert
        repositoryMock.Verify(r => r.EnqueueAsync(
            It.IsAny<OutboxEntry>(),
            It.IsAny<ITransactionContext?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void GivenDeliveryHandler_WhenProduce_ThenInvokesHandler()
    {
        // Arrange
        keySerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([1, 2, 3]);
        valueSerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([4, 5, 6]);

        var producer = CreateProducer();
        var message = new Message<string, string> { Key = "key", Value = "value" };
        DeliveryReport<string, string>? capturedReport = null;

        // Act
        producer.Produce("test-topic", message, report => capturedReport = report);

        // Assert
        Assert.NotNull(capturedReport);
        Assert.Equal("test-topic", capturedReport.Topic);
        Assert.Equal(PersistenceStatus.Persisted, capturedReport.Status);
    }

    [Fact]
    public void GivenNoDeliveryHandler_WhenProduce_ThenSucceeds()
    {
        // Arrange
        keySerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([1, 2, 3]);
        valueSerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([4, 5, 6]);

        var producer = CreateProducer();
        var message = new Message<string, string> { Key = "key", Value = "value" };

        // Act & Assert (no exception)
        producer.Produce("test-topic", message);
    }

    #endregion

    #region ExtractClusterIdentifier Tests

    [Theory]
    [InlineData(null, "unknown")]
    [InlineData("", "unknown")]
    [InlineData("   ", "unknown")]
    public void GivenNullOrEmptyBootstrapServers_WhenExtractClusterIdentifier_ThenReturnsUnknown(string? bootstrapServers, string expected)
    {
        // Arrange & Act
        var result = KafkaProducer<string, string>.ExtractClusterIdentifier(bootstrapServers);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GivenSingleServer_WhenExtractClusterIdentifier_ThenReturnsServer()
    {
        // Arrange & Act
        var result = KafkaProducer<string, string>.ExtractClusterIdentifier("localhost:9092");

        // Assert
        Assert.Equal("localhost:9092", result);
    }

    [Fact]
    public void GivenMultipleServers_WhenExtractClusterIdentifier_ThenReturnsFirstServer()
    {
        // Arrange & Act
        var result = KafkaProducer<string, string>.ExtractClusterIdentifier("broker1:9092,broker2:9092,broker3:9092");

        // Assert
        Assert.Equal("broker1:9092", result);
    }

    [Fact]
    public void GivenServerWithSpaces_WhenExtractClusterIdentifier_ThenReturnsTrimmedServer()
    {
        // Arrange & Act
        var result = KafkaProducer<string, string>.ExtractClusterIdentifier("  localhost:9092  ,  broker2:9092  ");

        // Assert
        Assert.Equal("localhost:9092", result);
    }

    [Fact]
    public void GivenEmptyFirstServer_WhenExtractClusterIdentifier_ThenReturnsUnknown()
    {
        // Arrange & Act
        var result = KafkaProducer<string, string>.ExtractClusterIdentifier(",broker2:9092");

        // Assert
        Assert.Equal("unknown", result);
    }

    #endregion

    #region GroupKey Tests

    [Fact]
    public async Task GivenTopicAndCluster_WhenProduceAsync_ThenGroupKeyIsClusterColonTopic()
    {
        // Arrange
        keySerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([1, 2, 3]);
        valueSerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([4, 5, 6]);

        var producer = CreateProducer();
        var message = new Message<string, string> { Key = "key", Value = "value" };

        string? capturedGroupKey = null;
        repositoryMock.Setup(r => r.GetNextSequenceAsync(It.IsAny<string>(), It.IsAny<ITransactionContext?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ITransactionContext?, CancellationToken>((gk, _, _) => capturedGroupKey = gk)
            .ReturnsAsync(1L);

        // Act
        await producer.ProduceAsync("orders", message);

        // Assert
        Assert.Equal("localhost:9092:orders", capturedGroupKey);
    }

    #endregion

    #region Message Headers Tests

    [Fact]
    public async Task GivenMessageWithHeaders_WhenProduceAsync_ThenHeadersAreStored()
    {
        // Arrange
        keySerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([1, 2, 3]);
        valueSerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([4, 5, 6]);

        var producer = CreateProducer();
        var message = new Message<string, string>
        {
            Key = "key",
            Value = "value",
            Headers =
            [
                new Header("correlation-id", [1, 2, 3, 4]),
                new Header("source", [5, 6, 7, 8])
            ]
        };

        OutboxEntry? capturedEntry = null;
        repositoryMock.Setup(r => r.EnqueueAsync(It.IsAny<OutboxEntry>(), It.IsAny<ITransactionContext?>(), It.IsAny<CancellationToken>()))
            .Callback<OutboxEntry, ITransactionContext?, CancellationToken>((e, _, _) => capturedEntry = e)
            .Returns(Task.CompletedTask);

        // Act
        await producer.ProduceAsync("test-topic", message);

        // Assert
        Assert.NotNull(capturedEntry);
        Assert.NotEmpty(capturedEntry.Payload);
        // The payload is MessagePack serialized, so we verify it was created successfully
    }

    #endregion

    #region Properties Tests

    [Fact]
    public async Task GivenMessage_WhenProduceAsync_ThenPropertiesContainTypeInfo()
    {
        // Arrange
        keySerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([1, 2, 3]);
        valueSerializerMock.Setup(s => s.Serialize(It.IsAny<string>(), It.IsAny<SerializationContext>()))
            .Returns([4, 5, 6]);

        var producer = CreateProducer();
        var message = new Message<string, string> { Key = "key", Value = "value" };

        OutboxEntry? capturedEntry = null;
        repositoryMock.Setup(r => r.EnqueueAsync(It.IsAny<OutboxEntry>(), It.IsAny<ITransactionContext?>(), It.IsAny<CancellationToken>()))
            .Callback<OutboxEntry, ITransactionContext?, CancellationToken>((e, _, _) => capturedEntry = e)
            .Returns(Task.CompletedTask);

        // Act
        await producer.ProduceAsync("test-topic", message);

        // Assert
        Assert.NotNull(capturedEntry);
        Assert.Equal("test-topic", capturedEntry.Properties["topic"]);
        Assert.Equal(clusterIdentifier, capturedEntry.Properties["cluster"]);
        Assert.Contains("String", capturedEntry.Properties["keyType"]);
        Assert.Contains("String", capturedEntry.Properties["valueType"]);
    }

    #endregion
}

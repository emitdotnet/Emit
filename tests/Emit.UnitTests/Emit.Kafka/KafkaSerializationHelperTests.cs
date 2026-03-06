namespace Emit.Kafka.Tests;

using global::Emit.Kafka;
using Moq;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public sealed class KafkaSerializationHelperTests
{
    [Fact]
    public async Task GivenSyncDeserializer_WhenDeserializeAsync_ThenReturnsDeserializedValue()
    {
        // Arrange
        var data = new ReadOnlyMemory<byte>(System.Text.Encoding.UTF8.GetBytes("hello"));

        // Act
        var result = await KafkaSerializationHelper.DeserializeAsync(
            data,
            isNull: false,
            topic: "test-topic",
            headers: new ConfluentKafka.Headers(),
            syncDeserializer: ConfluentKafka.Deserializers.Utf8,
            asyncDeserializer: null,
            componentType: ConfluentKafka.MessageComponentType.Value);

        // Assert
        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task GivenAsyncDeserializer_WhenDeserializeAsync_ThenReturnsDeserializedValue()
    {
        // Arrange
        var mockDeserializer = new Mock<ConfluentKafka.IAsyncDeserializer<string>>();
        mockDeserializer.Setup(d => d.DeserializeAsync(
            It.IsAny<ReadOnlyMemory<byte>>(),
            It.IsAny<bool>(),
            It.IsAny<ConfluentKafka.SerializationContext>()))
            .ReturnsAsync("deserialized-value");
        var data = new ReadOnlyMemory<byte>(System.Text.Encoding.UTF8.GetBytes("test"));

        // Act
        var result = await KafkaSerializationHelper.DeserializeAsync(
            data,
            isNull: false,
            topic: "test-topic",
            headers: new ConfluentKafka.Headers(),
            syncDeserializer: null,
            asyncDeserializer: mockDeserializer.Object,
            componentType: ConfluentKafka.MessageComponentType.Value);

        // Assert
        Assert.Equal("deserialized-value", result);
    }

    [Fact]
    public async Task GivenNoDeserializer_WhenDeserializeAsync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var data = new ReadOnlyMemory<byte>(System.Text.Encoding.UTF8.GetBytes("test"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            KafkaSerializationHelper.DeserializeAsync<string>(
                data,
                isNull: false,
                topic: "test-topic",
                headers: new ConfluentKafka.Headers(),
                syncDeserializer: null,
                asyncDeserializer: null,
                componentType: ConfluentKafka.MessageComponentType.Value).AsTask());
    }

    [Fact]
    public async Task GivenIsNullTrue_WhenDeserializeAsync_ThenHandlesNullData()
    {
        // Arrange
        var data = ReadOnlyMemory<byte>.Empty;

        // Act
        var result = await KafkaSerializationHelper.DeserializeAsync<string>(
            data,
            isNull: true,
            topic: "test-topic",
            headers: new ConfluentKafka.Headers(),
            syncDeserializer: ConfluentKafka.Deserializers.Utf8,
            asyncDeserializer: null,
            componentType: ConfluentKafka.MessageComponentType.Value);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GivenDeserializeAsync_WhenCalled_ThenPassesCorrectSerializationContext()
    {
        // Arrange
        ConfluentKafka.SerializationContext? capturedContext = null;
        var mockDeserializer = new Mock<ConfluentKafka.IAsyncDeserializer<string>>();
        mockDeserializer.Setup(d => d.DeserializeAsync(
            It.IsAny<ReadOnlyMemory<byte>>(),
            It.IsAny<bool>(),
            It.IsAny<ConfluentKafka.SerializationContext>()))
            .Callback<ReadOnlyMemory<byte>, bool, ConfluentKafka.SerializationContext>((_, _, ctx) => capturedContext = ctx)
            .ReturnsAsync("test");
        var data = new ReadOnlyMemory<byte>(System.Text.Encoding.UTF8.GetBytes("test"));

        // Act
        await KafkaSerializationHelper.DeserializeAsync(
            data,
            isNull: false,
            topic: "test-topic",
            headers: new ConfluentKafka.Headers(),
            syncDeserializer: null,
            asyncDeserializer: mockDeserializer.Object,
            componentType: ConfluentKafka.MessageComponentType.Key);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal("test-topic", capturedContext.Value.Topic);
        Assert.Equal(ConfluentKafka.MessageComponentType.Key, capturedContext.Value.Component);
    }

    [Fact]
    public async Task GivenDeserializeAsync_WhenCalledWithHeaders_ThenHeadersIncludedInContext()
    {
        // Arrange
        ConfluentKafka.SerializationContext? capturedContext = null;
        var mockDeserializer = new Mock<ConfluentKafka.IAsyncDeserializer<string>>();
        mockDeserializer.Setup(d => d.DeserializeAsync(
            It.IsAny<ReadOnlyMemory<byte>>(),
            It.IsAny<bool>(),
            It.IsAny<ConfluentKafka.SerializationContext>()))
            .Callback<ReadOnlyMemory<byte>, bool, ConfluentKafka.SerializationContext>((_, _, ctx) => capturedContext = ctx)
            .ReturnsAsync("test");
        var headers = new ConfluentKafka.Headers
        {
            { "key1", System.Text.Encoding.UTF8.GetBytes("value1") },
            { "key2", System.Text.Encoding.UTF8.GetBytes("value2") }
        };
        var data = new ReadOnlyMemory<byte>(System.Text.Encoding.UTF8.GetBytes("test"));

        // Act
        await KafkaSerializationHelper.DeserializeAsync(
            data,
            isNull: false,
            topic: "test-topic",
            headers: headers,
            syncDeserializer: null,
            asyncDeserializer: mockDeserializer.Object,
            componentType: ConfluentKafka.MessageComponentType.Value);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.NotNull(capturedContext.Value.Headers);
        Assert.Equal(2, capturedContext.Value.Headers.Count);
    }
}

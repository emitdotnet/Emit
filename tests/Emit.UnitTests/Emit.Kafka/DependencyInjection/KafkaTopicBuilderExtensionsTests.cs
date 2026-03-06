namespace Emit.Kafka.Tests.DependencyInjection;

using global::Emit.Kafka.DependencyInjection;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public sealed class KafkaTopicBuilderExtensionsTests
{
    // ── Built-in: Utf8 ──

    [Fact]
    public void GivenUtf8KeySerializer_WhenCalled_ThenSetsKeySerializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, int>("test-topic");

        // Act
        builder.SetUtf8KeySerializer();

        // Assert
        Assert.Same(ConfluentKafka.Serializers.Utf8, builder.KeySerializer);
    }

    [Fact]
    public void GivenUtf8ValueSerializer_WhenCalled_ThenSetsValueSerializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<int, string>("test-topic");

        // Act
        builder.SetUtf8ValueSerializer();

        // Assert
        Assert.Same(ConfluentKafka.Serializers.Utf8, builder.ValueSerializer);
    }

    [Fact]
    public void GivenUtf8KeyDeserializer_WhenCalled_ThenSetsKeyDeserializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, int>("test-topic");

        // Act
        builder.SetUtf8KeyDeserializer();

        // Assert
        Assert.Same(ConfluentKafka.Deserializers.Utf8, builder.KeyDeserializer);
    }

    [Fact]
    public void GivenUtf8ValueDeserializer_WhenCalled_ThenSetsValueDeserializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<int, string>("test-topic");

        // Act
        builder.SetUtf8ValueDeserializer();

        // Assert
        Assert.Same(ConfluentKafka.Deserializers.Utf8, builder.ValueDeserializer);
    }

    // ── Built-in: ByteArray ──

    [Fact]
    public void GivenByteArrayKeySerializer_WhenCalled_ThenSetsKeySerializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<byte[], string>("test-topic");

        // Act
        builder.SetByteArrayKeySerializer();

        // Assert
        Assert.Same(ConfluentKafka.Serializers.ByteArray, builder.KeySerializer);
    }

    [Fact]
    public void GivenByteArrayValueDeserializer_WhenCalled_ThenSetsValueDeserializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, byte[]>("test-topic");

        // Act
        builder.SetByteArrayValueDeserializer();

        // Assert
        Assert.Same(ConfluentKafka.Deserializers.ByteArray, builder.ValueDeserializer);
    }

    // ── Built-in: Null ──

    [Fact]
    public void GivenNullKeySerializer_WhenCalled_ThenSetsKeySerializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<ConfluentKafka.Null, string>("test-topic");

        // Act
        builder.SetNullKeySerializer();

        // Assert
        Assert.Same(ConfluentKafka.Serializers.Null, builder.KeySerializer);
    }

    // ── Built-in: Int32 ──

    [Fact]
    public void GivenInt32KeySerializer_WhenCalled_ThenSetsKeySerializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<int, string>("test-topic");

        // Act
        builder.SetInt32KeySerializer();

        // Assert
        Assert.Same(ConfluentKafka.Serializers.Int32, builder.KeySerializer);
    }

    [Fact]
    public void GivenInt32ValueDeserializer_WhenCalled_ThenSetsValueDeserializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, int>("test-topic");

        // Act
        builder.SetInt32ValueDeserializer();

        // Assert
        Assert.Same(ConfluentKafka.Deserializers.Int32, builder.ValueDeserializer);
    }

    // ── Built-in: Int64 ──

    [Fact]
    public void GivenInt64ValueSerializer_WhenCalled_ThenSetsValueSerializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, long>("test-topic");

        // Act
        builder.SetInt64ValueSerializer();

        // Assert
        Assert.Same(ConfluentKafka.Serializers.Int64, builder.ValueSerializer);
    }

    // ── Built-in: Single ──

    [Fact]
    public void GivenSingleKeyDeserializer_WhenCalled_ThenSetsKeyDeserializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<float, string>("test-topic");

        // Act
        builder.SetSingleKeyDeserializer();

        // Assert
        Assert.Same(ConfluentKafka.Deserializers.Single, builder.KeyDeserializer);
    }

    // ── Built-in: Double ──

    [Fact]
    public void GivenDoubleValueSerializer_WhenCalled_ThenSetsValueSerializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, double>("test-topic");

        // Act
        builder.SetDoubleValueSerializer();

        // Assert
        Assert.Same(ConfluentKafka.Serializers.Double, builder.ValueSerializer);
    }

    // ── Built-in: Ignore ──

    [Fact]
    public void GivenIgnoreKeyDeserializer_WhenCalled_ThenSetsKeyDeserializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<ConfluentKafka.Ignore, string>("test-topic");

        // Act
        builder.SetIgnoreKeyDeserializer();

        // Assert
        Assert.Same(ConfluentKafka.Deserializers.Ignore, builder.KeyDeserializer);
    }

    [Fact]
    public void GivenIgnoreValueDeserializer_WhenCalled_ThenSetsValueDeserializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, ConfluentKafka.Ignore>("test-topic");

        // Act
        builder.SetIgnoreValueDeserializer();

        // Assert
        Assert.Same(ConfluentKafka.Deserializers.Ignore, builder.ValueDeserializer);
    }
}

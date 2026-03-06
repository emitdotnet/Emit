namespace Emit.Kafka.AvroSerializer.Tests;

using global::Emit.Pipeline;
using global::Emit.Kafka.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using ConfluentKafka = Confluent.Kafka;
using ConfluentSchemaRegistry = Confluent.SchemaRegistry;
using ConfluentSerdes = Confluent.SchemaRegistry.Serdes;

public sealed class KafkaTopicBuilderAvroExtensionsTests
{
    // ── Schema registry validation ──

    [Fact]
    public void GivenAvroKeySerializer_WhenSchemaRegistryConfigured_ThenSetsFactory()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act
        builder.SetAvroKeySerializer();

        // Assert
        Assert.NotNull(builder.KeyAsyncSerializerFactory);
    }

    [Fact]
    public void GivenAvroKeySerializer_WhenSchemaRegistryNotConfigured_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var kafkaBuilder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            kafkaBuilder.Topic<string, string>("test-topic", topic =>
            {
                topic.SetAvroKeySerializer();
            }));
        Assert.Contains("ConfigureSchemaRegistry", ex.Message);
    }

    [Fact]
    public void GivenAvroValueSerializer_WhenSchemaRegistryConfigured_ThenSetsFactory()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act
        builder.SetAvroValueSerializer();

        // Assert
        Assert.NotNull(builder.ValueAsyncSerializerFactory);
    }

    [Fact]
    public void GivenAvroValueSerializer_WhenSchemaRegistryNotConfigured_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var kafkaBuilder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            kafkaBuilder.Topic<string, string>("test-topic", topic =>
            {
                topic.SetAvroValueSerializer();
            }));
        Assert.Contains("ConfigureSchemaRegistry", ex.Message);
    }

    [Fact]
    public void GivenAvroKeyDeserializer_WhenSchemaRegistryConfigured_ThenSetsFactory()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act
        builder.SetAvroKeyDeserializer();

        // Assert
        Assert.NotNull(builder.KeyAsyncDeserializerFactory);
    }

    [Fact]
    public void GivenAvroKeyDeserializer_WhenSchemaRegistryNotConfigured_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var kafkaBuilder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            kafkaBuilder.Topic<string, string>("test-topic", topic =>
            {
                topic.SetAvroKeyDeserializer();
            }));
        Assert.Contains("ConfigureSchemaRegistry", ex.Message);
    }

    [Fact]
    public void GivenAvroValueDeserializer_WhenSchemaRegistryConfigured_ThenSetsFactory()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act
        builder.SetAvroValueDeserializer();

        // Assert
        Assert.NotNull(builder.ValueAsyncDeserializerFactory);
    }

    [Fact]
    public void GivenAvroValueDeserializer_WhenSchemaRegistryNotConfigured_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var kafkaBuilder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            kafkaBuilder.Topic<string, string>("test-topic", topic =>
            {
                topic.SetAvroValueDeserializer();
            }));
        Assert.Contains("ConfigureSchemaRegistry", ex.Message);
    }

    // ── Factory resolution ──

    [Fact]
    public void GivenAvroKeySerializer_WhenFactoryInvoked_ThenReturnsAsyncSerializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        builder.SetAvroKeySerializer();
        var mockClient = new Mock<ConfluentSchemaRegistry.ISchemaRegistryClient>().Object;

        // Act
        var serializer = builder.KeyAsyncSerializerFactory!(mockClient);

        // Assert
        Assert.NotNull(serializer);
    }

    [Fact]
    public void GivenAvroValueDeserializer_WhenFactoryInvoked_ThenReturnsAsyncDeserializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        builder.SetAvroValueDeserializer();
        var mockClient = new Mock<ConfluentSchemaRegistry.ISchemaRegistryClient>().Object;

        // Act
        var deserializer = builder.ValueAsyncDeserializerFactory!(mockClient);

        // Assert
        Assert.NotNull(deserializer);
    }

    // ── Typed config overload ──

    [Fact]
    public void GivenAvroKeySerializerWithTypedConfig_WhenSchemaRegistryConfigured_ThenSetsFactory()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        var config = new ConfluentSerdes.AvroSerializerConfig
        {
            AutoRegisterSchemas = false,
        };

        // Act
        builder.SetAvroKeySerializer(config);

        // Assert
        Assert.NotNull(builder.KeyAsyncSerializerFactory);
    }

    // ── Mutual exclusivity: factory after sync/async ──

    [Fact]
    public void GivenSyncKeySerializer_WhenAvroKeySerializer_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        builder.SetUtf8KeySerializer();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.SetAvroKeySerializer());
    }

    [Fact]
    public void GivenAsyncKeySerializer_WhenAvroKeySerializer_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        builder.SetKeySerializer(new Mock<ConfluentKafka.IAsyncSerializer<string>>().Object);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.SetAvroKeySerializer());
    }

    // ── Mutual exclusivity: sync/async after factory ──

    [Fact]
    public void GivenAvroKeySerializer_WhenSyncKeySerializer_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        builder.SetAvroKeySerializer();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            builder.SetKeySerializer(ConfluentKafka.Serializers.Utf8));
    }

    [Fact]
    public void GivenAvroKeySerializer_WhenAsyncKeySerializer_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        builder.SetAvroKeySerializer();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            builder.SetKeySerializer(new Mock<ConfluentKafka.IAsyncSerializer<string>>().Object));
    }

    [Fact]
    public void GivenAvroValueDeserializer_WhenSyncValueDeserializer_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        builder.SetAvroValueDeserializer();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            builder.SetValueDeserializer(ConfluentKafka.Deserializers.Utf8));
    }

    [Fact]
    public void GivenAvroValueDeserializer_WhenAsyncValueDeserializer_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        builder.SetAvroValueDeserializer();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            builder.SetValueDeserializer(new Mock<ConfluentKafka.IAsyncDeserializer<string>>().Object));
    }
}

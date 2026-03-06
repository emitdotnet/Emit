namespace Emit.Kafka.ProtobufSerializer.Tests;

using global::Emit.Pipeline;
using global::Emit.Kafka.DependencyInjection;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using ConfluentKafka = Confluent.Kafka;
using ConfluentSchemaRegistry = Confluent.SchemaRegistry;

public sealed class KafkaTopicBuilderProtobufExtensionsTests
{
    // ── Serializer: schema registry validation ──

    [Fact]
    public void GivenProtobufKeySerializer_WhenSchemaRegistryConfigured_ThenSetsFactory()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<Timestamp, Duration>("test-topic");

        // Act
        builder.SetProtobufKeySerializer();

        // Assert
        Assert.NotNull(builder.KeyAsyncSerializerFactory);
    }

    [Fact]
    public void GivenProtobufKeySerializer_WhenSchemaRegistryNotConfigured_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var kafkaBuilder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            kafkaBuilder.Topic<Timestamp, Duration>("test-topic", topic =>
            {
                topic.SetProtobufKeySerializer();
            }));
        Assert.Contains("ConfigureSchemaRegistry", ex.Message);
    }

    [Fact]
    public void GivenProtobufValueSerializer_WhenSchemaRegistryConfigured_ThenSetsFactory()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<Timestamp, Duration>("test-topic");

        // Act
        builder.SetProtobufValueSerializer();

        // Assert
        Assert.NotNull(builder.ValueAsyncSerializerFactory);
    }

    [Fact]
    public void GivenProtobufValueSerializer_WhenSchemaRegistryNotConfigured_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var kafkaBuilder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            kafkaBuilder.Topic<Timestamp, Duration>("test-topic", topic =>
            {
                topic.SetProtobufValueSerializer();
            }));
        Assert.Contains("ConfigureSchemaRegistry", ex.Message);
    }

    // ── Deserializer: no schema registry required ──

    [Fact]
    public void GivenProtobufKeyDeserializer_WhenSchemaRegistryNotConfigured_ThenSetsDeserializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<Timestamp, Duration>("test-topic");

        // Act
        builder.SetProtobufKeyDeserializer();

        // Assert
        Assert.NotNull(builder.KeyAsyncDeserializer);
    }

    [Fact]
    public void GivenProtobufValueDeserializer_WhenSchemaRegistryNotConfigured_ThenSetsDeserializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<Timestamp, Duration>("test-topic");

        // Act
        builder.SetProtobufValueDeserializer();

        // Assert
        Assert.NotNull(builder.ValueAsyncDeserializer);
    }

    // ── Factory resolution ──

    [Fact]
    public void GivenProtobufKeySerializer_WhenFactoryInvoked_ThenReturnsAsyncSerializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<Timestamp, Duration>("test-topic");
        builder.SetProtobufKeySerializer();
        var mockClient = new Mock<ConfluentSchemaRegistry.ISchemaRegistryClient>().Object;

        // Act
        var serializer = builder.KeyAsyncSerializerFactory!(mockClient);

        // Assert
        Assert.NotNull(serializer);
    }

    // ── Mutual exclusivity: sync/async after protobuf ──

    [Fact]
    public void GivenProtobufKeyDeserializer_WhenSyncKeyDeserializer_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<Timestamp, Duration>("test-topic");
        builder.SetProtobufKeyDeserializer();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            builder.SetKeyDeserializer(new Mock<ConfluentKafka.IDeserializer<Timestamp>>().Object));
    }

    [Fact]
    public void GivenProtobufValueDeserializer_WhenSyncValueDeserializer_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<Timestamp, Duration>("test-topic");
        builder.SetProtobufValueDeserializer();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            builder.SetValueDeserializer(new Mock<ConfluentKafka.IDeserializer<Duration>>().Object));
    }
}

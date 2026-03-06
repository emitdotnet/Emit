namespace Emit.Kafka.JsonSerializer.Tests;

using global::Emit.Kafka.DependencyInjection;
using global::Emit.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using ConfluentSchemaRegistry = Confluent.SchemaRegistry;

public sealed class KafkaTopicBuilderJsonSchemaExtensionsTests
{
    // ── Serializer: schema registry validation ──

    [Fact]
    public void GivenJsonSchemaKeySerializer_WhenSchemaRegistryConfigured_ThenSetsFactory()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act
        builder.SetJsonSchemaKeySerializer();

        // Assert
        Assert.NotNull(builder.KeyAsyncSerializerFactory);
    }

    [Fact]
    public void GivenJsonSchemaKeySerializer_WhenSchemaRegistryNotConfigured_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var kafkaBuilder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            kafkaBuilder.Topic<string, string>("test-topic", topic =>
            {
                topic.SetJsonSchemaKeySerializer();
            }));
        Assert.Contains("ConfigureSchemaRegistry", ex.Message);
    }

    [Fact]
    public void GivenJsonSchemaValueSerializer_WhenSchemaRegistryConfigured_ThenSetsFactory()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act
        builder.SetJsonSchemaValueSerializer();

        // Assert
        Assert.NotNull(builder.ValueAsyncSerializerFactory);
    }

    [Fact]
    public void GivenJsonSchemaValueSerializer_WhenSchemaRegistryNotConfigured_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var kafkaBuilder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            kafkaBuilder.Topic<string, string>("test-topic", topic =>
            {
                topic.SetJsonSchemaValueSerializer();
            }));
        Assert.Contains("ConfigureSchemaRegistry", ex.Message);
    }

    // ── Serializer with Schema overload ──

    [Fact]
    public void GivenJsonSchemaKeySerializerWithSchema_WhenSchemaRegistryConfigured_ThenSetsFactory()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        var schema = new ConfluentSchemaRegistry.Schema("{}", ConfluentSchemaRegistry.SchemaType.Json);

        // Act
        builder.SetJsonSchemaKeySerializer(schema);

        // Assert
        Assert.NotNull(builder.KeyAsyncSerializerFactory);
    }

    [Fact]
    public void GivenJsonSchemaKeySerializerWithSchema_WhenSchemaRegistryNotConfigured_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var kafkaBuilder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());
        var schema = new ConfluentSchemaRegistry.Schema("{}", ConfluentSchemaRegistry.SchemaType.Json);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            kafkaBuilder.Topic<string, string>("test-topic", topic =>
            {
                topic.SetJsonSchemaKeySerializer(schema);
            }));
        Assert.Contains("ConfigureSchemaRegistry", ex.Message);
    }

    // ── Deserializer: no schema registry required ──

    [Fact]
    public void GivenJsonSchemaKeyDeserializer_WhenSchemaRegistryNotConfigured_ThenSetsDeserializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act
        builder.SetJsonSchemaKeyDeserializer();

        // Assert
        Assert.NotNull(builder.KeyAsyncDeserializer);
    }

    [Fact]
    public void GivenJsonSchemaValueDeserializer_WhenSchemaRegistryNotConfigured_ThenSetsDeserializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");

        // Act
        builder.SetJsonSchemaValueDeserializer();

        // Assert
        Assert.NotNull(builder.ValueAsyncDeserializer);
    }

    // ── Deserializer with Schema: requires schema registry ──

    [Fact]
    public void GivenJsonSchemaKeyDeserializerWithSchema_WhenSchemaRegistryConfigured_ThenSetsFactory()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        var schema = new ConfluentSchemaRegistry.Schema("{}", ConfluentSchemaRegistry.SchemaType.Json);

        // Act
        builder.SetJsonSchemaKeyDeserializer(schema);

        // Assert
        Assert.NotNull(builder.KeyAsyncDeserializerFactory);
    }

    [Fact]
    public void GivenJsonSchemaKeyDeserializerWithSchema_WhenSchemaRegistryNotConfigured_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var kafkaBuilder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());
        var schema = new ConfluentSchemaRegistry.Schema("{}", ConfluentSchemaRegistry.SchemaType.Json);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            kafkaBuilder.Topic<string, string>("test-topic", topic =>
            {
                topic.SetJsonSchemaKeyDeserializer(schema);
            }));
        Assert.Contains("ConfigureSchemaRegistry", ex.Message);
    }

    // ── Factory resolution ──

    [Fact]
    public void GivenJsonSchemaKeySerializer_WhenFactoryInvoked_ThenReturnsAsyncSerializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        builder.SetJsonSchemaKeySerializer();
        var mockClient = new Mock<ConfluentSchemaRegistry.ISchemaRegistryClient>().Object;

        // Act
        var serializer = builder.KeyAsyncSerializerFactory!(mockClient);

        // Assert
        Assert.NotNull(serializer);
    }

    [Fact]
    public void GivenJsonSchemaValueDeserializer_WhenFactoryInvoked_ThenReturnsAsyncDeserializer()
    {
        // Arrange
        var builder = new KafkaTopicBuilder<string, string>("test-topic");
        builder.SetJsonSchemaValueDeserializer();
        var mockClient = new Mock<ConfluentSchemaRegistry.ISchemaRegistryClient>().Object;

        // Act — for the no-schema overload, deserializer is set directly (no factory)
        var deserializer = builder.ValueAsyncDeserializer;

        // Assert
        Assert.NotNull(deserializer);
    }
}

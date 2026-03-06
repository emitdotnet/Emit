namespace Emit.Kafka.Tests.DependencyInjection;

using global::Emit.Kafka.DependencyInjection;
using global::Emit.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ConfluentKafka = Confluent.Kafka;
using ConfluentSchemaRegistry = Confluent.SchemaRegistry;

public sealed class KafkaBuilderTests
{
    [Fact]
    public void GivenConfigureClient_WhenCalled_ThenStoresClientConfigAction()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act
        builder.ConfigureClient(config => { });

        // Assert
        Assert.NotNull(builder.ClientConfigAction);
    }

    [Fact]
    public void GivenConfigureClient_WhenChaining_ThenReturnsKafkaBuilder()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act
        var result = builder.ConfigureClient(config => { });

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenConfigureClientCalledTwice_WhenCalling_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());
        builder.ConfigureClient(config => { });

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.ConfigureClient(config => { }));
        Assert.Contains("already been called", ex.Message);
    }

    [Fact]
    public void GivenNullAction_WhenConfigureClient_ThenThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.ConfigureClient(null!));
    }

    [Fact]
    public void GivenTopic_WhenChaining_ThenReturnsKafkaBuilder()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act
        var result = builder.Topic<string, string>("orders", topic => { });

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenDuplicateTopicName_WhenTopic_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act & Assert
        builder.Topic<string, string>("orders", topic => { });
        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.Topic<string, string>("orders", topic => { }));
        Assert.Contains("orders", ex.Message);
    }

    [Fact]
    public void GivenDifferentTypesSameTopicName_WhenTopic_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act & Assert
        builder.Topic<string, string>("orders", topic => { });
        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.Topic<int, object>("orders", topic => { }));
        Assert.Contains("orders", ex.Message);
    }

    [Fact]
    public void GivenNullTopicName_WhenTopic_ThenThrowsArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => builder.Topic<string, string>(null!, topic => { }));
    }

    [Fact]
    public void GivenWhitespaceTopicName_WhenTopic_ThenThrowsArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act & Assert
        Assert.Throws<ArgumentException>(() => builder.Topic<string, string>("   ", topic => { }));
    }

    [Fact]
    public void GivenNullConfigureAction_WhenTopic_ThenThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.Topic<string, string>("orders", null!));
    }

    [Fact]
    public void GivenTopic_WhenConfigureActionRuns_ThenReceivesTopicBuilder()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());
        KafkaTopicBuilder<string, string>? capturedBuilder = null;

        // Act
        builder.Topic<string, string>("orders", topic =>
        {
            capturedBuilder = topic;
        });

        // Assert
        Assert.NotNull(capturedBuilder);
    }

    [Fact]
    public void GivenMultipleDistinctTopics_WhenTopic_ThenAllSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act & Assert (no exception)
        builder.Topic<string, string>("orders", topic => { });
        builder.Topic<string, string>("payments", topic => { });
        builder.Topic<string, string>("notifications", topic => { });
    }

    [Fact]
    public void GivenTopicNameCaseDifference_WhenTopic_ThenBothSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act & Assert (no exception)
        builder.Topic<string, string>("orders", topic => { });
        builder.Topic<string, string>("Orders", topic => { });
    }

    [Fact]
    public void GivenConfigureSchemaRegistry_WhenCalled_ThenStoresSchemaRegistryConfigAction()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act
        builder.ConfigureSchemaRegistry(config => { });

        // Assert
        Assert.NotNull(builder.SchemaRegistryConfigAction);
    }

    [Fact]
    public void GivenConfigureSchemaRegistry_WhenChaining_ThenReturnsKafkaBuilder()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act
        var result = builder.ConfigureSchemaRegistry(config => { });

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenConfigureSchemaRegistryCalledTwice_WhenCalling_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());
        builder.ConfigureSchemaRegistry(config => { });

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.ConfigureSchemaRegistry(config => { }));
        Assert.Contains("already been called", ex.Message);
    }

    [Fact]
    public void GivenNullAction_WhenConfigureSchemaRegistry_ThenThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.ConfigureSchemaRegistry(null!));
    }

    [Fact]
    public void GivenConfigureSchemaRegistry_WhenCalled_ThenRegistersSchemaRegistryClientInServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act
        builder.ConfigureSchemaRegistry(config =>
        {
            config.Url = "http://localhost:8081";
        });

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(ConfluentSchemaRegistry.ISchemaRegistryClient));
    }

    [Fact]
    public void GivenConfigureSchemaRegistry_WhenResolved_ThenReturnsSchemaRegistryClient()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());
        builder.ConfigureSchemaRegistry(config =>
        {
            config.Url = "http://localhost:8081";
        });

        using var provider = services.BuildServiceProvider();

        // Act
        var client = provider.GetService<ConfluentSchemaRegistry.ISchemaRegistryClient>();

        // Assert
        Assert.NotNull(client);
        Assert.IsType<ConfluentSchemaRegistry.CachedSchemaRegistryClient>(client);
    }

    [Fact]
    public void GivenDeadLetter_WhenCalled_ThenStoresDeadLetterConfig()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act
        builder.DeadLetter(options => { });

        // Assert
        Assert.NotNull(builder.DeadLetterConfig);
    }

    [Fact]
    public void GivenDeadLetter_WhenChaining_ThenReturnsKafkaBuilder()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act
        var result = builder.DeadLetter(options => { });

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenDeadLetterCalledTwice_WhenCalling_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());
        builder.DeadLetter(options => { });

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.DeadLetter(options => { }));
        Assert.Contains("already been called", ex.Message);
    }

    [Fact]
    public void GivenNullAction_WhenDeadLetter_ThenThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.DeadLetter(null!));
    }

    [Fact]
    public void GivenDeadLetter_WhenCustomConvention_ThenConfigApplied()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act
        builder.DeadLetter(options =>
        {
            options.TopicNamingConvention = source => $"dlq-{source}";
        });

        // Assert
        Assert.NotNull(builder.DeadLetterConfig);
        Assert.Equal("dlq-orders", builder.DeadLetterConfig.TopicNamingConvention("orders"));
    }

    [Fact]
    public void GivenDeadLetterNotCalled_WhenChecked_ThenDeadLetterConfigIsNull()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Assert
        Assert.Null(builder.DeadLetterConfig);
    }

    [Fact]
    public void GivenConfigureProducer_WhenCalled_ThenStoresProducerConfigAction()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act
        builder.ConfigureProducer(config => { });

        // Assert
        Assert.NotNull(builder.ProducerConfigAction);
    }

    [Fact]
    public void GivenConfigureProducer_WhenChaining_ThenReturnsKafkaBuilder()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act
        var result = builder.ConfigureProducer(config => { });

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenConfigureProducerCalledTwice_WhenCalling_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());
        builder.ConfigureProducer(config => { });

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.ConfigureProducer(config => { }));
        Assert.Contains("already been called", ex.Message);
    }

    [Fact]
    public void GivenNullAction_WhenConfigureProducer_ThenThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: true, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.ConfigureProducer(null!));
    }
}

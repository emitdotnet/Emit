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
    public void GivenDeadLetter_WhenCalled_ThenRegistersIDeadLetterSinkInServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());
        builder.ConfigureClient(c => c.BootstrapServers = "localhost:9092");

        // Act
        builder.DeadLetter("orders.dlt");

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(global::Emit.Abstractions.IDeadLetterSink));
    }

    [Fact]
    public void GivenDeadLetter_WhenCalled_ThenRegistersKafkaDeadLetterOptionsInServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());
        builder.ConfigureClient(c => c.BootstrapServers = "localhost:9092");

        // Act
        builder.DeadLetter("orders.dlt");

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(KafkaDeadLetterOptions));
    }

    [Fact]
    public void GivenDeadLetter_WhenChaining_ThenReturnsKafkaBuilder()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());
        builder.ConfigureClient(c => c.BootstrapServers = "localhost:9092");

        // Act
        var result = builder.DeadLetter("orders.dlt");

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenDeadLetterCalledTwice_WhenCalling_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());
        builder.ConfigureClient(c => c.BootstrapServers = "localhost:9092");
        builder.DeadLetter("orders.dlt");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.DeadLetter("other.dlt"));
        Assert.Contains("already been called", ex.Message);
    }

    [Fact]
    public void GivenDeadLetter_WhenNullTopicName_ThenThrowsArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => builder.DeadLetter(null!));
    }

    [Fact]
    public void GivenDeadLetter_WhenTopicAlreadyDeclared_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());
        builder.ConfigureClient(c => c.BootstrapServers = "localhost:9092");
        builder.Topic<string, string>("orders.dlt", t =>
        {
            t.SetKeySerializer(ConfluentKafka.Serializers.Utf8);
            t.SetValueSerializer(ConfluentKafka.Serializers.Utf8);
        });

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.DeadLetter("orders.dlt"));
        Assert.Contains("already been declared", ex.Message);
    }

    [Fact]
    public void GivenDeadLetter_WhenNoConfigureCallback_ThenTopicRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());
        builder.ConfigureClient(c => c.BootstrapServers = "localhost:9092");

        // Act
        builder.DeadLetter("dlq-topic");

        // Assert — DLQ topic appears in required topics
        Assert.Contains("dlq-topic", builder.GetRequiredTopics());
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

    // ── AutoProvision ──

    [Fact]
    public void GivenAutoProvision_WhenCalled_ThenAutoProvisionEnabledIsTrue()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act
        builder.AutoProvision();

        // Assert
        Assert.True(builder.AutoProvisionEnabled);
    }

    [Fact]
    public void GivenAutoProvision_WhenCalled_ThenReturnsSameBuilder()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act
        var result = builder.AutoProvision();

        // Assert
        Assert.Same(builder, result);
    }

    // ── Provisioning configs ──

    [Fact]
    public void GivenTopicWithProvisioning_WhenCalled_ThenProvisioningConfigsContainsTopic()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act
        builder.Topic<string, string>("orders", t =>
        {
            t.Provisioning(opts => opts.Retention = null);
        });

        // Assert
        var configs = builder.GetProvisioningConfigs();
        Assert.True(configs.ContainsKey("orders"));
        Assert.Null(configs["orders"].Retention);
    }

    [Fact]
    public void GivenTopicWithoutProvisioning_WhenCalled_ThenProvisioningConfigsDoesNotContainTopic()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());

        // Act
        builder.Topic<string, string>("orders", t => { });

        // Assert
        var configs = builder.GetProvisioningConfigs();
        Assert.False(configs.ContainsKey("orders"));
    }

    [Fact]
    public void GivenGetRequiredTopics_WhenDeadLetterConfigured_ThenIncludesDlqTopic()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());
        builder.ConfigureClient(c => c.BootstrapServers = "localhost:9092");

        // Act
        builder.DeadLetter("my-dlq-topic");

        // Assert
        Assert.Contains("my-dlq-topic", builder.GetRequiredTopics());
    }

    [Fact]
    public void GivenTopic_ThenDeadLetterWithSameName_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new KafkaBuilder(services, outboxEnabled: false, new MessagePipelineBuilder(), new MessagePipelineBuilder());
        builder.ConfigureClient(c => c.BootstrapServers = "localhost:9092");
        builder.Topic<string, string>("shared-topic", t =>
        {
            t.SetKeySerializer(ConfluentKafka.Serializers.Utf8);
            t.SetValueSerializer(ConfluentKafka.Serializers.Utf8);
        });

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.DeadLetter("shared-topic"));
        Assert.Contains("already been declared", ex.Message);
    }
}

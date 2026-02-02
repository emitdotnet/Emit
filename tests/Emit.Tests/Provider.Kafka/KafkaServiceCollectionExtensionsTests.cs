namespace Emit.Tests.Provider.Kafka;

using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Provider.Kafka;
using Emit.Provider.Kafka.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class KafkaServiceCollectionExtensionsTests
{
    private readonly IServiceCollection services;
    private readonly EmitBuilder builder;

    public KafkaServiceCollectionExtensionsTests()
    {
        services = new ServiceCollection();
        services.AddLogging();
        builder = new EmitBuilder(services);
    }

    // Helper to create EmitBuilder via reflection since constructor is internal
    private EmitBuilder CreateBuilder(IServiceCollection services)
    {
        var constructor = typeof(EmitBuilder).GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null,
            [typeof(IServiceCollection)],
            null);

        if (constructor is null)
        {
            throw new InvalidOperationException("EmitBuilder constructor not found");
        }

        return (EmitBuilder)constructor.Invoke([services]);
    }

    #region AddKafkaProducerFactory Parameter Validation Tests

    [Fact]
    public void GivenNullBuilder_WhenAddKafkaProducerFactory_ThenThrowsArgumentNullException()
    {
        // Arrange
        var producerConfig = new ProducerConfig { BootstrapServers = "localhost:9092" };

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            KafkaServiceCollectionExtensions.AddKafkaProducerFactory(
                null!,
                "test-key",
                producerConfig));
    }

    [Fact]
    public void GivenNullRegistrationKey_WhenAddKafkaProducerFactory_ThenThrowsArgumentException()
    {
        // Arrange
        var producerConfig = new ProducerConfig { BootstrapServers = "localhost:9092" };

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            builder.AddKafkaProducerFactory(null!, producerConfig));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GivenEmptyRegistrationKey_WhenAddKafkaProducerFactory_ThenThrowsArgumentException(string key)
    {
        // Arrange
        var producerConfig = new ProducerConfig { BootstrapServers = "localhost:9092" };

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            builder.AddKafkaProducerFactory(key, producerConfig));
    }

    [Fact]
    public void GivenNullProducerConfig_WhenAddKafkaProducerFactory_ThenThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            builder.AddKafkaProducerFactory("test-key", null!));
    }

    #endregion

    #region AddKafkaProducerFactory Registration Tests

    [Fact]
    public void GivenValidParameters_WhenAddKafkaProducerFactory_ThenRegistersKeyedFactory()
    {
        // Arrange
        var producerConfig = new ProducerConfig { BootstrapServers = "localhost:9092" };
        var registrationKey = "test-key";

        // Act
        var result = builder.AddKafkaProducerFactory(registrationKey, producerConfig);

        // Assert
        Assert.Same(builder, result);

        var provider = services.BuildServiceProvider();
        var factory = provider.GetKeyedService<KafkaProducerFactory>(registrationKey);
        Assert.NotNull(factory);
        Assert.Equal(registrationKey, factory.RegistrationKey);
    }

    [Fact]
    public void GivenDefaultRegistrationKey_WhenAddKafkaProducerFactoryWithoutKey_ThenUsesDefaultKey()
    {
        // Arrange
        var producerConfig = new ProducerConfig { BootstrapServers = "localhost:9092" };

        // Act
        builder.AddKafkaProducerFactory(producerConfig);

        // Assert
        var provider = services.BuildServiceProvider();
        var factory = provider.GetKeyedService<KafkaProducerFactory>(EmitConstants.DefaultRegistrationKey);
        Assert.NotNull(factory);
        Assert.Equal(EmitConstants.DefaultRegistrationKey, factory.RegistrationKey);
    }

    [Fact]
    public void GivenSchemaRegistryConfig_WhenAddKafkaProducerFactory_ThenFactoryIsCreatedWithSchemaRegistry()
    {
        // Arrange
        var producerConfig = new ProducerConfig { BootstrapServers = "localhost:9092" };
        var schemaRegistryConfig = new SchemaRegistryConfig { Url = "http://localhost:8081" };

        // Act
        builder.AddKafkaProducerFactory("test-key", producerConfig, schemaRegistryConfig);

        // Assert
        var provider = services.BuildServiceProvider();
        var factory = provider.GetKeyedService<KafkaProducerFactory>("test-key");
        Assert.NotNull(factory);
    }

    #endregion

    #region Provider Registration Tests

    [Fact]
    public void GivenFirstKafkaRegistration_WhenAddKafkaProducerFactory_ThenRegistersKafkaOutboxProvider()
    {
        // Arrange
        var producerConfig = new ProducerConfig { BootstrapServers = "localhost:9092" };

        // Act
        builder.AddKafkaProducerFactory("test-key", producerConfig);

        // Assert
        var provider = services.BuildServiceProvider();
        var outboxProvider = provider.GetService<IOutboxProvider>();
        Assert.NotNull(outboxProvider);
        Assert.IsType<KafkaOutboxProvider>(outboxProvider);
    }

    [Fact]
    public void GivenMultipleKafkaRegistrations_WhenAddKafkaProducerFactory_ThenProviderIsRegisteredOnlyOnce()
    {
        // Arrange
        var producerConfig1 = new ProducerConfig { BootstrapServers = "localhost:9092" };
        var producerConfig2 = new ProducerConfig { BootstrapServers = "localhost:9093" };

        // Act
        builder.AddKafkaProducerFactory("key1", producerConfig1);
        builder.AddKafkaProducerFactory("key2", producerConfig2);

        // Assert - Only one IOutboxProvider registration
        var registrations = services.Where(d =>
            d.ServiceType == typeof(IOutboxProvider) &&
            d.ImplementationType == typeof(KafkaOutboxProvider));
        Assert.Single(registrations);
    }

    #endregion

    #region Method Chaining Tests

    [Fact]
    public void GivenAddKafkaProducerFactory_WhenCalled_ThenReturnsBuilderForChaining()
    {
        // Arrange
        var producerConfig = new ProducerConfig { BootstrapServers = "localhost:9092" };

        // Act
        var result = builder.AddKafkaProducerFactory("key", producerConfig);

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenAddKafkaProducerFactoryWithoutKey_WhenCalled_ThenReturnsBuilderForChaining()
    {
        // Arrange
        var producerConfig = new ProducerConfig { BootstrapServers = "localhost:9092" };

        // Act
        var result = builder.AddKafkaProducerFactory(producerConfig);

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region Multiple Factory Registration Tests

    [Fact]
    public void GivenMultipleFactories_WhenRegistering_ThenAllCanBeResolved()
    {
        // Arrange
        var producerConfig1 = new ProducerConfig { BootstrapServers = "broker1:9092" };
        var producerConfig2 = new ProducerConfig { BootstrapServers = "broker2:9092" };
        var producerConfig3 = new ProducerConfig { BootstrapServers = "broker3:9092" };

        // Act
        builder.AddKafkaProducerFactory("cluster-a", producerConfig1);
        builder.AddKafkaProducerFactory("cluster-b", producerConfig2);
        builder.AddKafkaProducerFactory(producerConfig3); // Default key

        // Assert
        var provider = services.BuildServiceProvider();

        var factoryA = provider.GetKeyedService<KafkaProducerFactory>("cluster-a");
        var factoryB = provider.GetKeyedService<KafkaProducerFactory>("cluster-b");
        var factoryDefault = provider.GetKeyedService<KafkaProducerFactory>(EmitConstants.DefaultRegistrationKey);

        Assert.NotNull(factoryA);
        Assert.NotNull(factoryB);
        Assert.NotNull(factoryDefault);

        Assert.Equal("cluster-a", factoryA.RegistrationKey);
        Assert.Equal("cluster-b", factoryB.RegistrationKey);
        Assert.Equal(EmitConstants.DefaultRegistrationKey, factoryDefault.RegistrationKey);
    }

    #endregion
}

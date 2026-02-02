namespace Emit.Tests.Provider.Kafka;

using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Emit.Provider.Kafka;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

public class KafkaProducerFactoryTests
{
    private readonly Mock<ILogger<KafkaProducerFactory>> loggerMock;
    private readonly string registrationKey = "test-key";
    private readonly ProducerConfig producerConfig = new() { BootstrapServers = "localhost:9092" };

    public KafkaProducerFactoryTests()
    {
        loggerMock = new Mock<ILogger<KafkaProducerFactory>>();
    }

    #region Constructor Tests

    [Fact]
    public void GivenNullRegistrationKey_WhenCreating_ThenThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new KafkaProducerFactory(
                null!,
                producerConfig,
                null,
                loggerMock.Object));
        Assert.Equal("registrationKey", exception.ParamName);
    }

    [Fact]
    public void GivenNullProducerConfig_WhenCreating_ThenThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new KafkaProducerFactory(
                registrationKey,
                null!,
                null,
                loggerMock.Object));
        Assert.Equal("producerConfig", exception.ParamName);
    }

    [Fact]
    public void GivenNullLogger_WhenCreating_ThenThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new KafkaProducerFactory(
                registrationKey,
                producerConfig,
                null,
                null!));
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void GivenValidParameters_WhenCreating_ThenSucceeds()
    {
        // Arrange & Act
        var factory = new KafkaProducerFactory(
            registrationKey,
            producerConfig,
            null,
            loggerMock.Object);

        // Assert
        Assert.NotNull(factory);
    }

    [Fact]
    public void GivenSchemaRegistryConfig_WhenCreating_ThenSucceeds()
    {
        // Arrange
        var schemaRegistryConfig = new SchemaRegistryConfig { Url = "http://localhost:8081" };

        // Act
        var factory = new KafkaProducerFactory(
            registrationKey,
            producerConfig,
            schemaRegistryConfig,
            loggerMock.Object);

        // Assert
        Assert.NotNull(factory);
    }

    #endregion

    #region RegistrationKey Property Tests

    [Fact]
    public void GivenFactory_WhenAccessingRegistrationKey_ThenReturnsConfiguredKey()
    {
        // Arrange
        var factory = new KafkaProducerFactory(
            registrationKey,
            producerConfig,
            null,
            loggerMock.Object);

        // Act
        var key = factory.RegistrationKey;

        // Assert
        Assert.Equal(registrationKey, key);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void GivenUnusedFactory_WhenDisposing_ThenSucceeds()
    {
        // Arrange
        var factory = new KafkaProducerFactory(
            registrationKey,
            producerConfig,
            null,
            loggerMock.Object);

        // Act & Assert (no exception)
        factory.Dispose();
    }

    [Fact]
    public void GivenDisposedFactory_WhenCallingGetProducer_ThenThrowsObjectDisposedException()
    {
        // Arrange
        var factory = new KafkaProducerFactory(
            registrationKey,
            producerConfig,
            null,
            loggerMock.Object);
        factory.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => factory.GetProducer());
    }

    [Fact]
    public void GivenFactory_WhenDisposingMultipleTimes_ThenSucceeds()
    {
        // Arrange
        var factory = new KafkaProducerFactory(
            registrationKey,
            producerConfig,
            null,
            loggerMock.Object);

        // Act & Assert (no exception)
        factory.Dispose();
        factory.Dispose();
        factory.Dispose();
    }

    #endregion
}

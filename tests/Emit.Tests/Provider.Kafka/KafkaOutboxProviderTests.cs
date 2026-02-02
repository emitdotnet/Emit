namespace Emit.Tests.Provider.Kafka;

using Emit.Models;
using Emit.Provider.Kafka;
using Emit.Provider.Kafka.Serialization;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

public class KafkaOutboxProviderTests
{
    private readonly Mock<ILogger<KafkaOutboxProvider>> loggerMock;

    public KafkaOutboxProviderTests()
    {
        loggerMock = new Mock<ILogger<KafkaOutboxProvider>>();
    }

    private static OutboxEntry CreateValidEntry(string registrationKey = "__default__")
    {
        var payload = new KafkaPayload
        {
            Topic = "test-topic",
            KeyBytes = [1, 2, 3],
            ValueBytes = [4, 5, 6]
        };

        return new OutboxEntry
        {
            Id = Guid.NewGuid(),
            ProviderId = EmitConstants.Providers.Kafka,
            RegistrationKey = registrationKey,
            GroupKey = "localhost:9092:test-topic",
            Sequence = 1,
            Payload = MessagePackSerializer.Serialize(payload)
        };
    }

    #region Constructor Tests

    [Fact]
    public void GivenNullServiceProvider_WhenCreating_ThenThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new KafkaOutboxProvider(null!, loggerMock.Object));
        Assert.Equal("serviceProvider", exception.ParamName);
    }

    [Fact]
    public void GivenNullLogger_WhenCreating_ThenThrowsArgumentNullException()
    {
        // Arrange
        var serviceProviderMock = new Mock<IServiceProvider>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new KafkaOutboxProvider(serviceProviderMock.Object, null!));
        Assert.Equal("logger", exception.ParamName);
    }

    #endregion

    #region ProviderId Tests

    [Fact]
    public void GivenProvider_WhenAccessingProviderId_ThenReturnsKafka()
    {
        // Arrange
        var serviceProviderMock = new Mock<IServiceProvider>();
        var provider = new KafkaOutboxProvider(serviceProviderMock.Object, loggerMock.Object);

        // Act
        var providerId = provider.ProviderId;

        // Assert
        Assert.Equal(EmitConstants.Providers.Kafka, providerId);
        Assert.Equal("kafka", providerId);
    }

    #endregion

    #region ProcessAsync Parameter Validation Tests

    [Fact]
    public async Task GivenNullEntry_WhenProcessAsync_ThenThrowsArgumentNullException()
    {
        // Arrange
        var serviceProviderMock = new Mock<IServiceProvider>();
        var provider = new KafkaOutboxProvider(serviceProviderMock.Object, loggerMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            provider.ProcessAsync(null!));
    }

    #endregion

    #region ProcessAsync Payload Deserialization Tests

    [Fact]
    public async Task GivenInvalidPayload_WhenProcessAsync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var serviceProviderMock = new Mock<IServiceProvider>();
        var provider = new KafkaOutboxProvider(serviceProviderMock.Object, loggerMock.Object);
        var entry = new OutboxEntry
        {
            Id = Guid.NewGuid(),
            ProviderId = EmitConstants.Providers.Kafka,
            RegistrationKey = "__default__",
            GroupKey = "test-group",
            Sequence = 1,
            Payload = [0xFF, 0xFF, 0xFF] // Invalid MessagePack data
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ProcessAsync(entry));
        Assert.NotNull(exception.Message);
        Assert.Contains("Failed to deserialize Kafka payload", exception.Message);
        Assert.Contains(entry.Id.ToString()!, exception.Message!);
    }

    #endregion

    #region ProcessAsync Producer Resolution Tests

    [Fact]
    public async Task GivenNoProducerFactoryRegistered_WhenProcessAsync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var serviceProviderMock = new Mock<IServiceProvider>();

        // Setup IKeyedServiceProvider BEFORE accessing Object property
        var keyedServiceProviderMock = serviceProviderMock.As<IKeyedServiceProvider>();
        keyedServiceProviderMock
            .Setup(sp => sp.GetKeyedService(typeof(KafkaProducerFactory), "__default__"))
            .Returns((KafkaProducerFactory?)null);
        keyedServiceProviderMock
            .Setup(sp => sp.GetKeyedService(typeof(KafkaProducerFactory), EmitConstants.DefaultRegistrationKey))
            .Returns((KafkaProducerFactory?)null);

        var provider = new KafkaOutboxProvider(serviceProviderMock.Object, loggerMock.Object);
        var entry = CreateValidEntry();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ProcessAsync(entry));
        Assert.Contains("Producer factory not found", exception.Message);
    }

    // NOTE: Tests that verify fallback to default factory and message building
    // require a running Kafka broker to validate the full flow. These are covered
    // by integration tests instead.

    #endregion
}

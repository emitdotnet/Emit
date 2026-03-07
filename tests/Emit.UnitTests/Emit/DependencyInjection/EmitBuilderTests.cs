namespace Emit.UnitTests.DependencyInjection;

using Emit;
using global::Emit.DependencyInjection;
using global::Emit.Kafka.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class EmitBuilderTests
{
    [Fact]
    public void GivenDuplicateOutboxRegistration_WhenValidate_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);
        builder.Services.AddSingleton(new OutboxRegistrationMarker("MongoDB"));
        builder.Services.AddSingleton(new OutboxRegistrationMarker("PostgreSQL"));

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => builder.Validate());
        Assert.Contains("outbox is registered by multiple persistence providers", exception.Message);
    }

    [Fact]
    public void GivenDuplicateDistributedLockRegistration_WhenValidate_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);
        builder.Services.AddSingleton(new DistributedLockRegistrationMarker("MongoDB"));
        builder.Services.AddSingleton(new DistributedLockRegistrationMarker("PostgreSQL"));

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => builder.Validate());
        Assert.Contains("distributed lock is registered by multiple persistence providers", exception.Message);
    }

    [Fact]
    public void GivenNoOutboxProvider_WhenValidate_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);
        builder.Services.AddSingleton(new OutboxRegistrationMarker("MongoDB"));
        builder.Services.AddSingleton(new PersistenceProviderMarker("MongoDB"));

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => builder.Validate());
        Assert.Contains("No outbox provider has been registered", exception.Message);
    }


    [Fact]
    public void GivenValidConfiguration_WhenValidate_ThenSucceeds()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);
        builder.Services.AddSingleton(new OutboxRegistrationMarker("MongoDB"));
        builder.Services.AddSingleton(new PersistenceProviderMarker("MongoDB"));
        builder.Services.AddSingleton(new OutboxProviderMarker());

        // Act & Assert (no exception means success)
        builder.Validate();
    }

    [Fact]
    public void GivenKafka_WhenAdding_ThenRegistersOutboxProviderMarker()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);
        builder.Services.AddSingleton(new OutboxRegistrationMarker("MongoDB"));

        // Act
        builder.AddKafka(kafka =>
        {
            kafka.ConfigureClient(_ => { });
        });

        // Assert
        Assert.Contains(services, d => d.ImplementationInstance is OutboxProviderMarker);
    }

    [Fact]
    public void GivenAddKafkaCalledTwice_WhenAdding_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);
        builder.AddKafka(kafka =>
        {
            kafka.ConfigureClient(_ => { });
        });

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddKafka(kafka =>
            {
                kafka.ConfigureClient(_ => { });
            }));
        Assert.Contains("AddKafka has already been called", exception.Message);
    }

    [Fact]
    public void GivenDirectModeWithoutPersistence_WhenValidate_ThenSucceeds()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);
        builder.AddKafka(kafka =>
        {
            kafka.ConfigureClient(_ => { });
        });

        // Act & Assert (no exception means success)
        builder.Validate();
    }

    [Fact]
    public void GivenNullConfigure_WhenAddKafka_ThenThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.AddKafka(null!));
    }

    [Fact]
    public void GivenMultiplePersistenceProviders_WhenValidate_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);
        builder.Services.AddSingleton(new PersistenceProviderMarker("MongoDB"));
        builder.Services.AddSingleton(new PersistenceProviderMarker("EntityFrameworkCore"));

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => builder.Validate());
        Assert.Contains("Multiple persistence providers registered", exception.Message);
    }

    [Fact]
    public void GivenConfigureLeaderElection_WhenCalled_ThenReturnsSameBuilderForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act
        var result = builder.ConfigureLeaderElection(cfg =>
        {
            cfg.HeartbeatInterval = TimeSpan.FromSeconds(10);
        });

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenNullConfigure_WhenConfigureLeaderElection_ThenThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.ConfigureLeaderElection(null!));
    }
}

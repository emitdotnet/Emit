namespace Emit.Tests.DependencyInjection;

using Emit;
using Emit.DependencyInjection;
using Emit.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class KafkaBuilderTests
{
    private readonly IServiceProvider serviceProvider;

    public KafkaBuilderTests()
    {
        serviceProvider = new ServiceCollection().BuildServiceProvider();
    }

    [Fact]
    public void GivenDefaultRegistrationKey_WhenCreating_ThenIsDefaultReturnsTrue()
    {
        // Arrange & Act
        var builder = new KafkaBuilder(serviceProvider, EmitConstants.DefaultRegistrationKey);

        // Assert
        Assert.True(builder.IsDefault);
        Assert.Equal(EmitConstants.DefaultRegistrationKey, builder.RegistrationKey);
    }

    [Fact]
    public void GivenNamedRegistrationKey_WhenCreating_ThenIsDefaultReturnsFalse()
    {
        // Arrange & Act
        var builder = new KafkaBuilder(serviceProvider, "analytics");

        // Assert
        Assert.False(builder.IsDefault);
        Assert.Equal("analytics", builder.RegistrationKey);
    }

    [Fact]
    public void GivenAddProducer_WhenAdding_ThenProducerIsTracked()
    {
        // Arrange
        var builder = new KafkaBuilder(serviceProvider, EmitConstants.DefaultRegistrationKey);

        // Act
        builder.AddProducer<string, string>(producer => producer.Topic = "test");
        builder.AddProducer<int, object>(producer => producer.Topic = "another");

        // Assert
        Assert.Equal(2, builder.ProducerRegistrations.Count);
    }

    [Fact]
    public void GivenAddProducer_WhenChaining_ThenReturnsKafkaBuilder()
    {
        // Arrange
        var builder = new KafkaBuilder(serviceProvider, EmitConstants.DefaultRegistrationKey);

        // Act
        var result = builder
            .AddProducer<string, string>(_ => { })
            .AddProducer<int, object>(_ => { });

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenConfigureResilience_WhenBuilding_ThenPolicyIsConfigured()
    {
        // Arrange
        var builder = new KafkaBuilder(serviceProvider, EmitConstants.DefaultRegistrationKey);
        builder.ConfigureResilience(policy => policy
            .WithRetry(5, BackoffStrategy.Exponential, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(2)));

        // Act
        var policy = builder.BuildResiliencePolicy();

        // Assert
        Assert.NotNull(policy);
        Assert.Equal(5, policy.MaxRetryCount);
    }

    [Fact]
    public void GivenNoResilienceConfiguration_WhenBuilding_ThenReturnsNull()
    {
        // Arrange
        var builder = new KafkaBuilder(serviceProvider, EmitConstants.DefaultRegistrationKey);

        // Act
        var policy = builder.BuildResiliencePolicy();

        // Assert
        Assert.Null(policy);
    }

    [Fact]
    public void GivenConfigureResilience_WhenChaining_ThenReturnsKafkaBuilder()
    {
        // Arrange
        var builder = new KafkaBuilder(serviceProvider, EmitConstants.DefaultRegistrationKey);

        // Act
        var result = builder.ConfigureResilience(_ => { });

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenNullConfigure_WhenAddProducer_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new KafkaBuilder(serviceProvider, EmitConstants.DefaultRegistrationKey);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.AddProducer<string, string>(null!));
    }

    [Fact]
    public void GivenNullConfigure_WhenConfigureResilience_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new KafkaBuilder(serviceProvider, EmitConstants.DefaultRegistrationKey);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.ConfigureResilience(null!));
    }
}

namespace Emit.Tests.DependencyInjection;

using Emit;
using Emit.DependencyInjection;
using Emit.Resilience;
using Xunit;

public class ProducerBuilderTests
{
    [Fact]
    public void GivenProducerBuilder_WhenSettingTopic_ThenTopicIsStored()
    {
        // Arrange
        var builder = new ProducerBuilder<string, string>(EmitConstants.DefaultRegistrationKey);

        // Act
        builder.Topic = "orders";

        // Assert
        Assert.Equal("orders", builder.Topic);
    }

    [Fact]
    public void GivenProducerBuilder_WhenSettingClusterIdentifier_ThenClusterIdentifierIsStored()
    {
        // Arrange
        var builder = new ProducerBuilder<string, string>(EmitConstants.DefaultRegistrationKey);

        // Act
        builder.ClusterIdentifier = "production-cluster";

        // Assert
        Assert.Equal("production-cluster", builder.ClusterIdentifier);
    }

    [Fact]
    public void GivenConfigureResilience_WhenBuilding_ThenPolicyIsConfigured()
    {
        // Arrange
        var builder = new ProducerBuilder<string, string>(EmitConstants.DefaultRegistrationKey);
        builder.ConfigureResilience(policy => policy
            .WithRetry(2, BackoffStrategy.FixedInterval, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30))
            .WithCircuitBreaker(3, TimeSpan.FromMinutes(1)));

        // Act
        var policy = builder.BuildResiliencePolicy();

        // Assert
        Assert.NotNull(policy);
        Assert.Equal(2, policy.MaxRetryCount);
        Assert.Equal(BackoffStrategy.FixedInterval, policy.BackoffStrategy);
        Assert.Equal(3, policy.CircuitBreakerFailureThreshold);
    }

    [Fact]
    public void GivenNoResilienceConfiguration_WhenBuilding_ThenReturnsNull()
    {
        // Arrange
        var builder = new ProducerBuilder<string, string>(EmitConstants.DefaultRegistrationKey);

        // Act
        var policy = builder.BuildResiliencePolicy();

        // Assert
        Assert.Null(policy);
    }

    [Fact]
    public void GivenConfigureResilience_WhenChaining_ThenReturnsProducerBuilder()
    {
        // Arrange
        var builder = new ProducerBuilder<string, string>(EmitConstants.DefaultRegistrationKey);

        // Act
        var result = builder.ConfigureResilience(_ => { });

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenProducerBuilder_WhenGettingProducerKey_ThenIncludesTypesAndRegistrationKey()
    {
        // Arrange
        var builder = new ProducerBuilder<string, int>("analytics");

        // Act
        var key = builder.GetProducerKey();

        // Assert
        Assert.Contains("analytics", key);
        Assert.Contains(typeof(string).FullName!, key);
        Assert.Contains(typeof(int).FullName!, key);
    }

    [Fact]
    public void GivenDifferentTypes_WhenGettingProducerKey_ThenKeysAreDifferent()
    {
        // Arrange
        var builder1 = new ProducerBuilder<string, int>(EmitConstants.DefaultRegistrationKey);
        var builder2 = new ProducerBuilder<string, string>(EmitConstants.DefaultRegistrationKey);

        // Act
        var key1 = builder1.GetProducerKey();
        var key2 = builder2.GetProducerKey();

        // Assert
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GivenDifferentRegistrationKeys_WhenGettingProducerKey_ThenKeysAreDifferent()
    {
        // Arrange
        var builder1 = new ProducerBuilder<string, string>(EmitConstants.DefaultRegistrationKey);
        var builder2 = new ProducerBuilder<string, string>("analytics");

        // Act
        var key1 = builder1.GetProducerKey();
        var key2 = builder2.GetProducerKey();

        // Assert
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GivenNullConfigure_WhenConfigureResilience_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new ProducerBuilder<string, string>(EmitConstants.DefaultRegistrationKey);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.ConfigureResilience(null!));
    }
}

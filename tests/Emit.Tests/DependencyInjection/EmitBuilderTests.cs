namespace Emit.Tests.DependencyInjection;

using Emit;
using Emit.DependencyInjection;
using Emit.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class EmitBuilderTests
{
    [Fact]
    public void GivenNoPersistenceProvider_WhenValidate_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);
        builder.AddKafka((_, kafka) => kafka.AddProducer<string, string>(_ => { }));

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => builder.Validate());
        Assert.Contains("No persistence provider has been registered", exception.Message);
    }

    [Fact]
    public void GivenNoOutboxProvider_WhenValidate_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);
        builder.UseMongoDb((_, options) => options.ConnectionString = "mongodb://localhost");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => builder.Validate());
        Assert.Contains("No outbox provider has been registered", exception.Message);
    }

    [Fact]
    public void GivenMongoDbAndPostgreSql_WhenRegisteringPostgreSqlSecond_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);
        builder.UseMongoDb((_, options) => options.ConnectionString = "mongodb://localhost");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.UsePostgreSql((_, options) => options.ConnectionString = "Host=localhost"));
        Assert.Contains("Cannot register PostgreSQL", exception.Message);
        Assert.Contains("MongoDB", exception.Message);
    }

    [Fact]
    public void GivenPostgreSqlAndMongoDb_WhenRegisteringMongoDbSecond_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);
        builder.UsePostgreSql((_, options) => options.ConnectionString = "Host=localhost");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.UseMongoDb((_, options) => options.ConnectionString = "mongodb://localhost"));
        Assert.Contains("Cannot register MongoDB", exception.Message);
        Assert.Contains("PostgreSQL", exception.Message);
    }

    [Fact]
    public void GivenValidConfiguration_WhenValidate_ThenSucceeds()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);
        builder.UseMongoDb((_, options) => options.ConnectionString = "mongodb://localhost");
        builder.AddKafka((_, kafka) => kafka.AddProducer<string, string>(_ => { }));

        // Act & Assert (no exception means success)
        builder.Validate();
    }

    [Fact]
    public void GivenDefaultKafka_WhenAdding_ThenUsesDefaultRegistrationKey()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);
        KafkaBuilder? capturedKafkaBuilder = null;

        builder.AddKafka((sp, kafka) =>
        {
            capturedKafkaBuilder = kafka;
            kafka.AddProducer<string, string>(_ => { });
        });

        // Act
        builder.UseMongoDb((_, _) => { });
        builder.KafkaRegistrations[0](new ServiceCollection().BuildServiceProvider());

        // Assert
        Assert.NotNull(capturedKafkaBuilder);
        Assert.Equal(EmitConstants.DefaultRegistrationKey, capturedKafkaBuilder.RegistrationKey);
        Assert.True(capturedKafkaBuilder.IsDefault);
    }

    [Fact]
    public void GivenNamedKafka_WhenAdding_ThenUsesProvidedKey()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);
        KafkaBuilder? capturedKafkaBuilder = null;
        var key = "analytics";

        builder.AddKafka(key, (sp, kafka) =>
        {
            capturedKafkaBuilder = kafka;
            kafka.AddProducer<string, string>(_ => { });
        });

        // Act
        builder.UseMongoDb((_, _) => { });
        builder.KafkaRegistrations[0](new ServiceCollection().BuildServiceProvider());

        // Assert
        Assert.NotNull(capturedKafkaBuilder);
        Assert.Equal(key, capturedKafkaBuilder.RegistrationKey);
        Assert.False(capturedKafkaBuilder.IsDefault);
    }

    [Fact]
    public void GivenMultipleKafkaRegistrations_WhenAdding_ThenAllAreTracked()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act
        builder.AddKafka((_, kafka) => kafka.AddProducer<string, string>(_ => { }));
        builder.AddKafka("analytics", (_, kafka) => kafka.AddProducer<string, int>(_ => { }));
        builder.AddKafka("logging", (_, kafka) => kafka.AddProducer<string, object>(_ => { }));

        // Assert
        Assert.Equal(3, builder.KafkaRegistrations.Count);
        Assert.True(builder.HasOutboxProvider);
    }

    [Fact]
    public void GivenConfigureResilience_WhenBuildingPolicy_ThenPolicyIsConfigured()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);
        builder.ConfigureResilience(policy => policy
            .WithRetry(3, BackoffStrategy.FixedInterval, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1))
            .WithCircuitBreaker(2, TimeSpan.FromMinutes(3)));

        // Act
        var policy = builder.BuildGlobalResiliencePolicy();

        // Assert
        Assert.NotNull(policy);
        Assert.Equal(3, policy.MaxRetryCount);
        Assert.Equal(BackoffStrategy.FixedInterval, policy.BackoffStrategy);
        Assert.Equal(TimeSpan.FromSeconds(2), policy.BackoffBaseDelay);
        Assert.Equal(2, policy.CircuitBreakerFailureThreshold);
    }

    [Fact]
    public void GivenNoResilienceConfiguration_WhenBuildingPolicy_ThenReturnsNull()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act
        var policy = builder.BuildGlobalResiliencePolicy();

        // Assert
        Assert.Null(policy);
    }

    [Fact]
    public void GivenNullConfigure_WhenUseMongoDb_ThenThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.UseMongoDb(null!));
    }

    [Fact]
    public void GivenNullConfigure_WhenUsePostgreSql_ThenThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.UsePostgreSql(null!));
    }

    [Fact]
    public void GivenNullConfigure_WhenAddKafka_ThenThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.AddKafka((Action<IServiceProvider, KafkaBuilder>)null!));
    }

    [Fact]
    public void GivenNullKey_WhenAddKafkaWithKey_ThenThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.AddKafka(null!, (_, _) => { }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GivenEmptyOrWhitespaceKey_WhenAddKafkaWithKey_ThenThrowsArgumentException(string key)
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => builder.AddKafka(key, (_, _) => { }));
    }
}

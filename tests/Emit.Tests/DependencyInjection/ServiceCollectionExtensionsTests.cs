namespace Emit.Tests.DependencyInjection;

using Emit.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void GivenNullServices_WhenAddEmit_ThenThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => services!.AddEmit(_ => { }));
    }

    [Fact]
    public void GivenNullConfigure_WhenAddEmit_ThenThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => services.AddEmit(null!));
    }

    [Fact]
    public void GivenValidConfiguration_WhenAddEmit_ThenReturnsServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddEmit(builder =>
        {
            builder.UseMongoDb((_, options) => options.ConnectionString = "mongodb://localhost");
            builder.AddKafka((_, kafka) => kafka.AddProducer<string, string>(_ => { }));
        });

        // Assert
        Assert.Same(services, result);
    }

    [Fact]
    public void GivenNoPersistenceProvider_WhenAddEmit_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddEmit(builder =>
            {
                builder.AddKafka((_, kafka) => kafka.AddProducer<string, string>(_ => { }));
            }));
        Assert.Contains("No persistence provider", exception.Message);
    }

    [Fact]
    public void GivenNoOutboxProvider_WhenAddEmit_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddEmit(builder =>
            {
                builder.UseMongoDb((_, options) => options.ConnectionString = "mongodb://localhost");
            }));
        Assert.Contains("No outbox provider", exception.Message);
    }

    [Fact]
    public void GivenBothPersistenceProviders_WhenAddEmit_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            services.AddEmit(builder =>
            {
                builder.UseMongoDb((_, _) => { });
                builder.UsePostgreSql((_, _) => { });
            }));
    }
}

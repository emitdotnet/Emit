namespace Emit.MongoDB.Tests.DependencyInjection;

using global::Emit.Abstractions;
using global::Emit.DependencyInjection;
using global::Emit.MongoDB;
using global::Emit.MongoDB.Configuration;
using global::Emit.MongoDB.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

public class MongoDbEmitBuilderExtensionsTests
{
    [Fact]
    public void GivenNullBuilder_WhenAddMongoDb_ThenThrowsArgumentNullException()
    {
        // Arrange
        EmitBuilder builder = null!;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => MongoDbEmitBuilderExtensions.AddMongoDb(builder, _ => { }));
        Assert.Equal("builder", exception.ParamName);
    }

    [Fact]
    public void GivenNullConfigure_WhenAddMongoDb_ThenThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => builder.AddMongoDb(null!));
        Assert.Equal("configure", exception.ParamName);
    }

    [Fact]
    public void GivenNoConfigure_WhenAddMongoDb_ThenThrows()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddMongoDb(_ => { }));
        Assert.Contains("Configure", exception.Message);
    }

    [Fact]
    public void GivenValidConfiguration_WhenAddMongoDb_ThenRegistersMongoDbContext()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act
        builder.AddMongoDb(mongo => mongo.Configure((_, _) => { }));

        // Assert
        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(MongoDbContext));

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void GivenNoUseOutboxOrUseDistributedLock_WhenAddMongoDb_ThenDoesNotRegisterOutboxOrLock()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act
        builder.AddMongoDb(mongo => mongo.Configure((_, _) => { }));

        // Assert
        var outboxDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxRepository));
        var lockDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IDistributedLockProvider));

        Assert.Null(outboxDescriptor);
        Assert.Null(lockDescriptor);
    }

    [Fact]
    public void GivenUseOutbox_WhenAddMongoDb_ThenRegistersOutboxRepository()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act
        builder.AddMongoDb(mongo =>
        {
            mongo.Configure((_, _) => { });
            mongo.UseOutbox();
        });

        // Assert
        var outboxDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxRepository));
        Assert.NotNull(outboxDescriptor);
        Assert.Equal(ServiceLifetime.Scoped, outboxDescriptor.Lifetime);
    }

    [Fact]
    public void GivenUseDistributedLock_WhenAddMongoDb_ThenRegistersNonKeyedLock()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act
        builder.AddMongoDb(mongo =>
        {
            mongo.Configure((_, _) => { });
            mongo.UseDistributedLock();
        });

        // Assert
        var nonKeyedLockDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IDistributedLockProvider) &&
            d.ServiceKey == null);
        Assert.NotNull(nonKeyedLockDescriptor);
        Assert.Equal(ServiceLifetime.Singleton, nonKeyedLockDescriptor.Lifetime);
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
                builder.AddMongoDb(mongo =>
                {
                    mongo.Configure((_, _) => { });
                    mongo.UseOutbox();
                });
            }));
        Assert.Contains("No outbox provider", exception.Message);
    }

    [Fact]
    public void GivenMongoOutboxEnabled_WhenServicesBuilt_ThenIUnitOfWorkRegisteredAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act
        builder.AddMongoDb(mongo =>
        {
            mongo.Configure((_, _) => { });
            mongo.UseOutbox();
        });

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IUnitOfWork));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void GivenMongoOutboxEnabled_WhenServicesBuilt_ThenIMongoSessionAccessorRegisteredAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act
        builder.AddMongoDb(mongo =>
        {
            mongo.Configure((_, _) => { });
            mongo.UseOutbox();
        });

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMongoSessionAccessor));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void GivenMongoOutboxNotEnabled_WhenServicesBuilt_ThenIUnitOfWorkNotRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act
        builder.AddMongoDb(mongo => mongo.Configure((_, _) => { }));

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IUnitOfWork));
        Assert.Null(descriptor);
    }

    [Fact]
    public void GivenMongoOutboxNotEnabled_WhenServicesBuilt_ThenIMongoSessionAccessorNotRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act
        builder.AddMongoDb(mongo => mongo.Configure((_, _) => { }));

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMongoSessionAccessor));
        Assert.Null(descriptor);
    }
}

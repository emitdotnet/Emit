namespace Emit.EntityFrameworkCore.Tests.DependencyInjection;

using global::Emit.Abstractions;
using global::Emit.DependencyInjection;
using global::Emit.EntityFrameworkCore.DependencyInjection;
using global::Emit.EntityFrameworkCore.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

internal sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddEmitModel(emit => emit.UseNpgsql());
    }
}

public class EntityFrameworkCoreEmitBuilderExtensionsTests
{
    private static void RegisterDbContext(IServiceCollection services)
    {
        // Register DbContext and Factory with dummy options for unit tests
        services.AddDbContext<TestDbContext>(options => options.UseNpgsql("Host=localhost;Database=test"));
        services.AddDbContextFactory<TestDbContext>(options => options.UseNpgsql("Host=localhost;Database=test"));
    }

    [Fact]
    public void GivenNullBuilder_WhenAddEntityFrameworkCore_ThenThrowsArgumentNullException()
    {
        // Arrange
        EmitBuilder builder = null!;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => builder.AddEntityFrameworkCore<TestDbContext>(ef => ef.UseNpgsql()));
        Assert.Equal("builder", exception.ParamName);
    }

    [Fact]
    public void GivenNullConfigure_WhenAddEntityFrameworkCore_ThenThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => builder.AddEntityFrameworkCore<TestDbContext>(null!));
        Assert.Equal("configure", exception.ParamName);
    }

    [Fact]
    public void GivenNoDatabaseProvider_WhenAddEntityFrameworkCore_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        RegisterDbContext(services);
        var builder = new EmitBuilder(services);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddEntityFrameworkCore<TestDbContext>(_ => { }));
        Assert.Contains("No database provider was configured", exception.Message);
        Assert.Contains("UseNpgsql()", exception.Message);
    }

    [Fact]
    public void GivenNoUseOutboxOrUseDistributedLock_WhenAddEntityFrameworkCore_ThenDoesNotRegisterOutboxOrLock()
    {
        // Arrange
        var services = new ServiceCollection();
        RegisterDbContext(services);
        var builder = new EmitBuilder(services);

        // Act
        builder.AddEntityFrameworkCore<TestDbContext>(ef => ef.UseNpgsql());

        // Assert
        var outboxDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxRepository));
        var lockDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IDistributedLockProvider));
        var cleanupWorkerDescriptor = services.FirstOrDefault(d => d.ImplementationType == typeof(LockCleanupWorker<TestDbContext>));

        Assert.Null(outboxDescriptor);
        Assert.Null(lockDescriptor);
        Assert.Null(cleanupWorkerDescriptor);
    }

    [Fact]
    public void GivenUseOutbox_WhenAddEntityFrameworkCore_ThenRegistersOutboxRepository()
    {
        // Arrange
        var services = new ServiceCollection();
        RegisterDbContext(services);
        var builder = new EmitBuilder(services);

        // Act
        builder.AddEntityFrameworkCore<TestDbContext>(ef =>
        {
            ef.UseNpgsql();
            ef.UseOutbox();
        });

        // Assert
        var outboxDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxRepository));
        Assert.NotNull(outboxDescriptor);
        Assert.Equal(ServiceLifetime.Scoped, outboxDescriptor.Lifetime);
    }

    [Fact]
    public void GivenUseDistributedLock_WhenAddEntityFrameworkCore_ThenRegistersNonKeyedLock()
    {
        // Arrange
        var services = new ServiceCollection();
        RegisterDbContext(services);
        var builder = new EmitBuilder(services);

        // Act
        builder.AddEntityFrameworkCore<TestDbContext>(ef =>
        {
            ef.UseNpgsql();
            ef.UseDistributedLock();
        });

        // Assert
        var nonKeyedLockDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IDistributedLockProvider) &&
            d.ServiceKey == null);
        Assert.NotNull(nonKeyedLockDescriptor);
        Assert.Equal(ServiceLifetime.Singleton, nonKeyedLockDescriptor.Lifetime);
    }

    [Fact]
    public void GivenUseDistributedLock_WhenAddEntityFrameworkCore_ThenRegistersLockCleanupWorker()
    {
        // Arrange
        var services = new ServiceCollection();
        RegisterDbContext(services);
        var builder = new EmitBuilder(services);

        // Act
        builder.AddEntityFrameworkCore<TestDbContext>(ef =>
        {
            ef.UseNpgsql();
            ef.UseDistributedLock();
        });

        // Assert
        var cleanupWorkerDescriptor = services.FirstOrDefault(d => d.ImplementationType == typeof(LockCleanupWorker<TestDbContext>));
        Assert.NotNull(cleanupWorkerDescriptor);
        Assert.Equal(ServiceLifetime.Singleton, cleanupWorkerDescriptor.Lifetime);
    }

    [Fact]
    public void GivenValidConfiguration_WhenAddEntityFrameworkCore_ThenRegistersPersistenceProviderMarker()
    {
        // Arrange
        var services = new ServiceCollection();
        RegisterDbContext(services);
        var builder = new EmitBuilder(services);

        // Act
        builder.AddEntityFrameworkCore<TestDbContext>(ef => ef.UseNpgsql());

        // Assert
        Assert.Contains(services, d =>
            d.ImplementationInstance is EmitBuilder.PersistenceProviderMarker { ProviderName: "EntityFrameworkCore" });
    }
}

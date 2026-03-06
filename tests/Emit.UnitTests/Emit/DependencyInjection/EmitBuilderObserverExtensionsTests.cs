namespace Emit.UnitTests.DependencyInjection;

using global::Emit.Abstractions.Observability;
using global::Emit.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class EmitBuilderObserverExtensionsTests
{
    [Fact]
    public void GivenBuilder_WhenAddProduceObserver_ThenObserverRegisteredInServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act
        builder.AddProduceObserver<TestProduceObserver>();

        // Assert
        var descriptor = services.SingleOrDefault(d =>
            d.ServiceType == typeof(IProduceObserver) &&
            d.ImplementationType == typeof(TestProduceObserver));

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void GivenBuilder_WhenAddConsumeObserver_ThenObserverRegisteredInServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act
        builder.AddConsumeObserver<TestConsumeObserver>();

        // Assert
        var descriptor = services.SingleOrDefault(d =>
            d.ServiceType == typeof(IConsumeObserver) &&
            d.ImplementationType == typeof(TestConsumeObserver));

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void GivenBuilder_WhenAddOutboxObserver_ThenObserverRegisteredInServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new EmitBuilder(services);

        // Act
        builder.AddOutboxObserver<TestOutboxObserver>();

        // Assert
        var descriptor = services.SingleOrDefault(d =>
            d.ServiceType == typeof(IOutboxObserver) &&
            d.ImplementationType == typeof(TestOutboxObserver));

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    private sealed class TestProduceObserver : IProduceObserver;

    private sealed class TestConsumeObserver : IConsumeObserver;

    private sealed class TestOutboxObserver : IOutboxObserver;
}

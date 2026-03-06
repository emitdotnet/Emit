namespace Emit.Mediator.Tests;

using global::Emit.DependencyInjection;
using global::Emit.Mediator;
using global::Emit.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class MediatorEmitBuilderExtensionsTests
{
    [Fact]
    public void GivenValidConfiguration_WhenAddMediator_ThenRegistersIMediator()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddEmit(emit => emit.AddMediator(m => m.AddHandler<TestHandler>()));

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(IMediator));
    }

    [Fact]
    public void GivenValidConfiguration_WhenAddMediator_ThenRegistersMediatorAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddEmit(emit => emit.AddMediator(m => m.AddHandler<TestHandler>()));

        // Assert
        var descriptor = services.Single(d => d.ServiceType == typeof(IMediator));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void GivenValidConfiguration_WhenAddMediator_ThenRegistersMediatorConfigurationAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddEmit(emit => emit.AddMediator(m => m.AddHandler<TestHandler>()));

        // Assert
        var descriptor = services.Single(d => d.ServiceType == typeof(MediatorConfiguration));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void GivenDoubleRegistration_WhenAddMediator_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            services.AddEmit(emit =>
            {
                emit.AddMediator(m => m.AddHandler<TestHandler>());
                emit.AddMediator(m => m.AddHandler<TestHandler>());
            }));
    }

    private sealed record TestRequest(string Value) : IRequest<string>;

    private sealed class TestHandler : IRequestHandler<TestRequest, string>
    {
        public Task<string> HandleAsync(TestRequest request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value);
    }
}

namespace Emit.UnitTests.Abstractions.Pipeline;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using Xunit;

public sealed class MiddlewareDescriptorTests
{
    [Fact]
    public void GivenForTypeRuntime_WhenValidType_ThenTypeSet()
    {
        // Act
        var descriptor = MiddlewareDescriptor.ForType(typeof(TestMiddleware));

        // Assert
        Assert.Equal(typeof(TestMiddleware), descriptor.MiddlewareType);
        Assert.Null(descriptor.Factory);
        Assert.Equal(MiddlewareLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void GivenForTypeRuntime_WhenScopedLifetime_ThenLifetimeIsScoped()
    {
        // Act
        var descriptor = MiddlewareDescriptor.ForType(typeof(TestMiddleware), MiddlewareLifetime.Scoped);

        // Assert
        Assert.Equal(MiddlewareLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void GivenForTypeRuntime_WhenNull_ThenThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => MiddlewareDescriptor.ForType(null!));
    }

    [Fact]
    public void GivenForTypeRuntime_WhenNotIMiddleware_ThenThrowsArgumentException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => MiddlewareDescriptor.ForType(typeof(string)));
        Assert.Contains("IMiddleware", ex.Message);
    }

    [Fact]
    public void GivenForFactory_WhenValid_ThenFactorySetAndTypeNull()
    {
        // Arrange
        Func<IServiceProvider, IMiddleware<InboundContext<string>>> factory = _ => new TestMiddleware();

        // Act
        var descriptor = MiddlewareDescriptor.ForFactory(factory);

        // Assert
        Assert.Null(descriptor.MiddlewareType);
        Assert.Same(factory, descriptor.Factory);
        Assert.Equal(MiddlewareLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void GivenForFactory_WhenScopedLifetime_ThenLifetimeIsScoped()
    {
        // Arrange
        Func<IServiceProvider, IMiddleware<InboundContext<string>>> factory = _ => new TestMiddleware();

        // Act
        var descriptor = MiddlewareDescriptor.ForFactory(factory, MiddlewareLifetime.Scoped);

        // Assert
        Assert.Equal(MiddlewareLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void GivenForFactory_WhenNull_ThenThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => MiddlewareDescriptor.ForFactory((Func<IServiceProvider, IMiddleware<InboundContext<string>>>)null!));
    }

    private sealed class TestMiddleware : IMiddleware<InboundContext<string>>
    {
        public Task InvokeAsync(InboundContext<string> context, MessageDelegate<InboundContext<string>> next) => next(context);
    }
}

namespace Emit.UnitTests.Pipeline;

using System.Linq;
using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class MessagePipelineBuilderTests
{
    [Fact]
    public void GivenNewBuilder_WhenUseCalledMultipleTimes_ThenMiddlewareTypesCollected()
    {
        // Arrange
        var builder = new MessagePipelineBuilder();

        // Act
        builder.Use(typeof(MiddlewareA)).Use(typeof(MiddlewareB));

        // Assert
        Assert.Equal([typeof(MiddlewareA), typeof(MiddlewareB)], builder.Descriptors.Select(d => d.MiddlewareType).ToList());
    }

    [Fact]
    public void GivenUse_WhenCalled_ThenReturnsSameBuilderForChaining()
    {
        // Arrange
        var builder = new MessagePipelineBuilder();

        // Act
        var result = builder.Use(typeof(MiddlewareA));

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenMiddlewareTypes_WhenRegisterServices_ThenRegisteredAsSingletons()
    {
        // Arrange
        var builder = new MessagePipelineBuilder();
        builder.Use(typeof(MiddlewareA)).Use(typeof(MiddlewareB));
        var services = new ServiceCollection();

        // Act
        builder.RegisterServices(services);

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(MiddlewareA) && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(MiddlewareB) && d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void GivenRegisterServices_WhenCalledTwice_ThenDoesNotDuplicate()
    {
        // Arrange
        var builder = new MessagePipelineBuilder();
        builder.Use(typeof(MiddlewareA));
        var services = new ServiceCollection();

        // Act
        builder.RegisterServices(services);
        builder.RegisterServices(services);

        // Assert
        Assert.Single(services, d => d.ServiceType == typeof(MiddlewareA));
    }

    [Fact]
    public async Task GivenNoMiddleware_WhenBuild_ThenTerminalInvoked()
    {
        // Arrange
        var builder = new MessagePipelineBuilder();
        var provider = new ServiceCollection().BuildServiceProvider();
        var invoked = false;
        IMiddlewarePipeline<TestInboundContext> terminal = new TestPipeline<TestInboundContext>(_ => { invoked = true; return Task.CompletedTask; });

        // Act
        var pipeline = builder.Build<TestInboundContext, string>(provider, terminal);
        await pipeline.InvokeAsync(CreateContext(provider));

        // Assert
        Assert.True(invoked);
    }

    [Fact]
    public async Task GivenMiddleware_WhenBuild_ThenMiddlewareWrapsTerminal()
    {
        // Arrange
        var builder = new MessagePipelineBuilder();
        builder.Use(typeof(MiddlewareA));
        var services = new ServiceCollection();
        builder.RegisterServices(services);
        var provider = services.BuildServiceProvider();

        var order = new List<string>();
        MiddlewareA.Order = order;
        IMiddlewarePipeline<TestInboundContext> terminal = new TestPipeline<TestInboundContext>(_ => { order.Add("terminal"); return Task.CompletedTask; });

        // Act
        var pipeline = builder.Build<TestInboundContext, string>(provider, terminal);
        await pipeline.InvokeAsync(CreateContext(provider));

        // Assert
        Assert.Equal(["A:before", "terminal", "A:after"], order);
    }

    [Fact]
    public async Task GivenParentLayers_WhenBuild_ThenFlattensOutermostFirst()
    {
        // Arrange
        var globalBuilder = new MessagePipelineBuilder();
        globalBuilder.Use(typeof(MiddlewareA));

        var patternBuilder = new MessagePipelineBuilder();
        patternBuilder.Use(typeof(MiddlewareB));

        var groupBuilder = new MessagePipelineBuilder();

        var services = new ServiceCollection();
        globalBuilder.RegisterServices(services);
        patternBuilder.RegisterServices(services);
        var provider = services.BuildServiceProvider();

        var order = new List<string>();
        MiddlewareA.Order = order;
        MiddlewareB.Order = order;
        IMiddlewarePipeline<TestInboundContext> terminal = new TestPipeline<TestInboundContext>(_ => { order.Add("terminal"); return Task.CompletedTask; });

        // Act — groupBuilder.Build with globalBuilder and patternBuilder as parents
        var pipeline = groupBuilder.Build<TestInboundContext, string>(provider, terminal, globalBuilder, patternBuilder);
        await pipeline.InvokeAsync(CreateContext(provider));

        // Assert — global outermost, pattern inner, then terminal
        Assert.Equal(["A:before", "B:before", "terminal", "B:after", "A:after"], order);
    }

    [Fact]
    public async Task GivenAllThreeLayers_WhenBuild_ThenOrderIsGlobalPatternGroup()
    {
        // Arrange
        var globalBuilder = new MessagePipelineBuilder();
        globalBuilder.Use(typeof(MiddlewareA));

        var patternBuilder = new MessagePipelineBuilder();
        patternBuilder.Use(typeof(MiddlewareB));

        var groupBuilder = new MessagePipelineBuilder();
        groupBuilder.Use(typeof(MiddlewareC));

        var services = new ServiceCollection();
        globalBuilder.RegisterServices(services);
        patternBuilder.RegisterServices(services);
        groupBuilder.RegisterServices(services);
        var provider = services.BuildServiceProvider();

        var order = new List<string>();
        MiddlewareA.Order = order;
        MiddlewareB.Order = order;
        MiddlewareC.Order = order;
        IMiddlewarePipeline<TestInboundContext> terminal = new TestPipeline<TestInboundContext>(_ => { order.Add("terminal"); return Task.CompletedTask; });

        // Act
        var pipeline = groupBuilder.Build<TestInboundContext, string>(provider, terminal, globalBuilder, patternBuilder);
        await pipeline.InvokeAsync(CreateContext(provider));

        // Assert — global → pattern → group → terminal
        Assert.Equal(["A:before", "B:before", "C:before", "terminal", "C:after", "B:after", "A:after"], order);
    }

    [Fact]
    public void GivenUseWithLifetime_WhenCalled_ThenDescriptorHasCorrectLifetime()
    {
        // Arrange
        var builder = new MessagePipelineBuilder();

        // Act
        builder.Use(typeof(MiddlewareA), MiddlewareLifetime.Scoped);

        // Assert
        var descriptor = Assert.Single(builder.Descriptors);
        Assert.Equal(typeof(MiddlewareA), descriptor.MiddlewareType);
        Assert.Equal(MiddlewareLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void GivenUseWithLifetime_WhenCalled_ThenReturnsSameBuilderForChaining()
    {
        // Arrange
        var builder = new MessagePipelineBuilder();

        // Act
        var result = builder.Use(typeof(MiddlewareA), MiddlewareLifetime.Scoped);

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenUseWithFactory_WhenCalled_ThenDescriptorHasFactory()
    {
        // Arrange
        var builder = new MessagePipelineBuilder();
        Func<IServiceProvider, IMiddleware<TestInboundContext>> factory = _ => new MiddlewareA();

        // Act
        builder.Use(factory);

        // Assert
        var descriptor = Assert.Single(builder.Descriptors);
        Assert.Null(descriptor.MiddlewareType);
        Assert.Same(factory, descriptor.Factory);
        Assert.Equal(MiddlewareLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void GivenUseWithFactory_WhenNullFactory_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new MessagePipelineBuilder();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => builder.Use((Func<IServiceProvider, IMiddleware<TestInboundContext>>)null!));
    }

    [Fact]
    public void GivenUseWithFactory_WhenCalled_ThenReturnsSameBuilderForChaining()
    {
        // Arrange
        var builder = new MessagePipelineBuilder();

        // Act
        var result = builder.Use<TestInboundContext>(_ => new MiddlewareA());

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenScopedType_WhenRegisterServices_ThenRegisteredAsScoped()
    {
        // Arrange
        var builder = new MessagePipelineBuilder();
        builder.Use(typeof(MiddlewareA), MiddlewareLifetime.Scoped);
        var services = new ServiceCollection();

        // Act
        builder.RegisterServices(services);

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(MiddlewareA) && d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void GivenFactoryMiddleware_WhenRegisterServices_ThenNothingRegistered()
    {
        // Arrange
        var builder = new MessagePipelineBuilder();
        builder.Use<TestInboundContext>(_ => new MiddlewareA());
        var services = new ServiceCollection();

        // Act
        builder.RegisterServices(services);

        // Assert
        Assert.Empty(services);
    }

    [Fact]
    public async Task GivenSingletonFactory_WhenBuild_ThenFactoryCalledOnceAtBuildTime()
    {
        // Arrange
        var builder = new MessagePipelineBuilder();
        var callCount = 0;
        builder.Use<TestInboundContext>(_ =>
        {
            callCount++;
            return new PassthroughMiddleware();
        });

        var provider = new ServiceCollection().BuildServiceProvider();
        IMiddlewarePipeline<TestInboundContext> terminal = new TestPipeline<TestInboundContext>(_ => Task.CompletedTask);

        // Act
        var pipeline = builder.Build<TestInboundContext, string>(provider, terminal);
        Assert.Equal(1, callCount); // Factory called at build time

        await pipeline.InvokeAsync(CreateContext(provider));
        await pipeline.InvokeAsync(CreateContext(provider));

        // Assert — factory was only called once
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GivenScopedType_WhenBuild_ThenResolvedPerInvocationFromContextServices()
    {
        // Arrange
        var builder = new MessagePipelineBuilder();
        builder.Use(typeof(ScopedMiddleware), MiddlewareLifetime.Scoped);

        var services = new ServiceCollection();
        builder.RegisterServices(services);
        var provider = services.BuildServiceProvider();

        var instances = new List<ScopedMiddleware>();
        ScopedMiddleware.OnInvoke = mw => instances.Add(mw);
        IMiddlewarePipeline<TestInboundContext> terminal = new TestPipeline<TestInboundContext>(_ => Task.CompletedTask);

        // Act
        var pipeline = builder.Build<TestInboundContext, string>(provider, terminal);

        using var scope1 = provider.CreateScope();
        await pipeline.InvokeAsync(CreateContext(scope1.ServiceProvider));

        using var scope2 = provider.CreateScope();
        await pipeline.InvokeAsync(CreateContext(scope2.ServiceProvider));

        // Assert — different scopes produce different instances
        Assert.Equal(2, instances.Count);
        Assert.NotSame(instances[0], instances[1]);
    }

    [Fact]
    public async Task GivenScopedFactory_WhenBuild_ThenFactoryCalledPerInvocation()
    {
        // Arrange
        var builder = new MessagePipelineBuilder();
        var callCount = 0;
        builder.Use<TestInboundContext>(_ =>
        {
            callCount++;
            return new PassthroughMiddleware();
        }, MiddlewareLifetime.Scoped);

        var provider = new ServiceCollection().BuildServiceProvider();
        IMiddlewarePipeline<TestInboundContext> terminal = new TestPipeline<TestInboundContext>(_ => Task.CompletedTask);

        // Act
        var pipeline = builder.Build<TestInboundContext, string>(provider, terminal);
        Assert.Equal(0, callCount); // Not called at build time

        await pipeline.InvokeAsync(CreateContext(provider));
        await pipeline.InvokeAsync(CreateContext(provider));

        // Assert — factory called once per invocation
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GivenMixedLifetimes_WhenBuild_ThenCorrectExecutionOrder()
    {
        // Arrange
        var builder = new MessagePipelineBuilder();
        var order = new List<string>();

        // Singleton type
        builder.Use(typeof(MiddlewareA));

        // Scoped factory
        builder.Use<TestInboundContext>(_ => new OrderTrackingMiddleware("scoped-factory", order), MiddlewareLifetime.Scoped);

        // Singleton factory
        builder.Use<TestInboundContext>(_ => new OrderTrackingMiddleware("singleton-factory", order));

        var services = new ServiceCollection();
        builder.RegisterServices(services);
        var provider = services.BuildServiceProvider();

        MiddlewareA.Order = order;
        IMiddlewarePipeline<TestInboundContext> terminal = new TestPipeline<TestInboundContext>(_ => { order.Add("terminal"); return Task.CompletedTask; });

        // Act
        var pipeline = builder.Build<TestInboundContext, string>(provider, terminal);
        await pipeline.InvokeAsync(CreateContext(provider));

        // Assert — all three middlewares wrap the terminal in registration order
        Assert.Equal(
            ["A:before", "scoped-factory:before", "singleton-factory:before", "terminal", "singleton-factory:after", "scoped-factory:after", "A:after"],
            order);
    }

    private static TestInboundContext CreateContext(IServiceProvider services) => new()
    {
        MessageId = Guid.NewGuid().ToString(),
        Timestamp = DateTimeOffset.UtcNow,
        CancellationToken = CancellationToken.None,
        Services = services,
        Message = "test",
        TransportContext = TestTransportContext.Create(services),
    };

    private sealed class TestInboundContext : ConsumeContext<string>;

    private sealed class PassthroughMiddleware : IMiddleware<TestInboundContext>
    {
        public Task InvokeAsync(TestInboundContext context, IMiddlewarePipeline<TestInboundContext> next) => next.InvokeAsync(context);
    }

    private sealed class ScopedMiddleware : IMiddleware<TestInboundContext>
    {
        [ThreadStatic]
        internal static Action<ScopedMiddleware>? OnInvoke;

        public Task InvokeAsync(TestInboundContext context, IMiddlewarePipeline<TestInboundContext> next)
        {
            OnInvoke?.Invoke(this);
            return next.InvokeAsync(context);
        }
    }

    private sealed class OrderTrackingMiddleware(string name, List<string> order) : IMiddleware<TestInboundContext>
    {
        public async Task InvokeAsync(TestInboundContext context, IMiddlewarePipeline<TestInboundContext> next)
        {
            order.Add($"{name}:before");
            await next.InvokeAsync(context).ConfigureAwait(false);
            order.Add($"{name}:after");
        }
    }

    private sealed class MiddlewareA : IMiddleware<TestInboundContext>
    {
        [ThreadStatic]
        internal static List<string>? Order;

        public async Task InvokeAsync(TestInboundContext context, IMiddlewarePipeline<TestInboundContext> next)
        {
            Order?.Add("A:before");
            await next.InvokeAsync(context).ConfigureAwait(false);
            Order?.Add("A:after");
        }
    }

    private sealed class MiddlewareB : IMiddleware<TestInboundContext>
    {
        [ThreadStatic]
        internal static List<string>? Order;

        public async Task InvokeAsync(TestInboundContext context, IMiddlewarePipeline<TestInboundContext> next)
        {
            Order?.Add("B:before");
            await next.InvokeAsync(context).ConfigureAwait(false);
            Order?.Add("B:after");
        }
    }

    private sealed class MiddlewareC : IMiddleware<TestInboundContext>
    {
        [ThreadStatic]
        internal static List<string>? Order;

        public async Task InvokeAsync(TestInboundContext context, IMiddlewarePipeline<TestInboundContext> next)
        {
            Order?.Add("C:before");
            await next.InvokeAsync(context).ConfigureAwait(false);
            Order?.Add("C:after");
        }
    }
}

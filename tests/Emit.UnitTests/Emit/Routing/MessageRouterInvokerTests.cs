namespace Emit.UnitTests.Routing;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class MessageRouterInvokerTests
{
    private readonly ILogger<MessageRouterInvoker<string>> logger =
        NullLogger<MessageRouterInvoker<string>>.Instance;

    [Fact]
    public async Task GivenMatchingRoute_WhenInvoking_ThenDispatchesToMatchedPipeline()
    {
        // Arrange
        var dispatched = false;
        var routedPipelines = new Dictionary<object, (Type ConsumerType, MessageDelegate<InboundContext<string>> Pipeline)>
        {
            ["key-a"] = (typeof(ConsumerA), ctx => { dispatched = true; return Task.CompletedTask; }),
        };
        var invoker = CreateInvoker("test-router", ctx => (object?)"key-a", routedPipelines);
        var context = CreateContext("test-message");

        // Act
        await invoker.InvokeAsync(context);

        // Assert
        Assert.True(dispatched);
    }

    [Fact]
    public async Task GivenMatchingRoute_WhenInvoking_ThenSetsConsumerIdentityFeature()
    {
        // Arrange
        var routedPipelines = new Dictionary<object, (Type ConsumerType, MessageDelegate<InboundContext<string>> Pipeline)>
        {
            ["key-a"] = (typeof(ConsumerA), ctx => Task.CompletedTask),
        };
        var invoker = CreateInvoker("order-router", ctx => (object?)"key-a", routedPipelines);
        var context = CreateContext("test-message");

        // Act
        await invoker.InvokeAsync(context);

        // Assert
        var identity = context.Features.Get<IConsumerIdentityFeature>();
        Assert.NotNull(identity);
        Assert.Equal("order-router", identity.Identifier);
        Assert.Equal(ConsumerKind.Router, identity.Kind);
        Assert.Equal(typeof(ConsumerA), identity.ConsumerType);
        Assert.Equal("key-a", identity.RouteKey);
    }

    [Fact]
    public async Task GivenMatchingRoute_WhenInvoking_ThenRouteKeySetOnIdentityFeature()
    {
        // Arrange
        var routedPipelines = new Dictionary<object, (Type ConsumerType, MessageDelegate<InboundContext<string>> Pipeline)>
        {
            ["order.created"] = (typeof(ConsumerA), ctx => Task.CompletedTask),
        };
        var invoker = CreateInvoker("test-router", ctx => (object?)"order.created", routedPipelines);
        var context = CreateContext("test-message");

        // Act
        await invoker.InvokeAsync(context);

        // Assert
        var identity = context.Features.Get<IConsumerIdentityFeature>();
        Assert.NotNull(identity);
        Assert.Equal("order.created", identity.RouteKey);
    }

    [Fact]
    public async Task GivenNullRouteKey_WhenInvoking_ThenThrowsUnmatchedRouteException()
    {
        // Arrange
        var routedPipelines = new Dictionary<object, (Type ConsumerType, MessageDelegate<InboundContext<string>> Pipeline)>
        {
            ["key-a"] = (typeof(ConsumerA), ctx => Task.CompletedTask),
        };
        var invoker = CreateInvoker("test-router", ctx => null, routedPipelines);
        var context = CreateContext("test-message");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnmatchedRouteException>(
            () => invoker.InvokeAsync(context));
        Assert.Null(ex.RouteKey);
    }

    [Fact]
    public async Task GivenUnmatchedRouteKey_WhenInvoking_ThenThrowsWithRouteKey()
    {
        // Arrange
        var routedPipelines = new Dictionary<object, (Type ConsumerType, MessageDelegate<InboundContext<string>> Pipeline)>
        {
            ["key-a"] = (typeof(ConsumerA), ctx => Task.CompletedTask),
        };
        var invoker = CreateInvoker("test-router", ctx => (object?)"key-unknown", routedPipelines);
        var context = CreateContext("test-message");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnmatchedRouteException>(
            () => invoker.InvokeAsync(context));
        Assert.Equal("key-unknown", ex.RouteKey);
    }

    [Fact]
    public async Task GivenMultipleRoutes_WhenInvoking_ThenDispatchesToCorrectPipeline()
    {
        // Arrange
        var dispatchedA = false;
        var dispatchedB = false;
        var routedPipelines = new Dictionary<object, (Type ConsumerType, MessageDelegate<InboundContext<string>> Pipeline)>
        {
            ["key-a"] = (typeof(ConsumerA), ctx => { dispatchedA = true; return Task.CompletedTask; }),
            ["key-b"] = (typeof(ConsumerB), ctx => { dispatchedB = true; return Task.CompletedTask; }),
        };
        var invoker = CreateInvoker("test-router", ctx => (object?)"key-b", routedPipelines);
        var context = CreateContext("test-message");

        // Act
        await invoker.InvokeAsync(context);

        // Assert
        Assert.False(dispatchedA);
        Assert.True(dispatchedB);
    }

    // ── Test helpers ──

    private MessageRouterInvoker<string> CreateInvoker(
        string identifier,
        Func<InboundContext<string>, object?> selector,
        Dictionary<object, (Type ConsumerType, MessageDelegate<InboundContext<string>> Pipeline)> routedPipelines)
    {
        return new MessageRouterInvoker<string>(
            identifier, selector, routedPipelines, logger);
    }

    private static TestInboundContext CreateContext(string message)
    {
        return new TestInboundContext
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = new TestServiceProvider(),
            Message = message,
        };
    }

    private sealed class TestInboundContext : InboundContext<string>;

    private sealed class TestServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class ConsumerA : IConsumer<string>
    {
        public Task ConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class ConsumerB : IConsumer<string>
    {
        public Task ConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}

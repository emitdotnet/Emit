namespace Emit.UnitTests.Routing;

using System.Diagnostics;
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
        var routedPipelines = new Dictionary<object, (Type ConsumerType, IMiddlewarePipeline<ConsumeContext<string>> Pipeline)>
        {
            ["key-a"] = (typeof(ConsumerA), new TestPipeline<ConsumeContext<string>>(ctx => { dispatched = true; return Task.CompletedTask; })),
        };
        var invoker = CreateInvoker("test-router", ctx => (object?)"key-a", routedPipelines);
        var context = CreateContext("test-message");

        // Act
        await invoker.InvokeAsync(context);

        // Assert
        Assert.True(dispatched);
    }

    [Fact]
    public async Task GivenMatchingRoute_WhenActivityPresent_ThenSetsRouteKeyTag()
    {
        // Arrange
        using var activitySource = new ActivitySource("test");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        var routedPipelines = new Dictionary<object, (Type ConsumerType, IMiddlewarePipeline<ConsumeContext<string>> Pipeline)>
        {
            ["key-a"] = (typeof(ConsumerA), new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask)),
        };
        var invoker = CreateInvoker("order-router", ctx => (object?)"key-a", routedPipelines);
        var context = CreateContext("test-message");

        // Act
        using var activity = activitySource.StartActivity("test-consume");
        await invoker.InvokeAsync(context);

        // Assert
        Assert.NotNull(activity);
        Assert.Equal("key-a", activity.GetTagItem("emit.route.key"));
    }

    [Fact]
    public async Task GivenMatchingRoute_WhenNoActivity_ThenDoesNotThrow()
    {
        // Arrange
        var routedPipelines = new Dictionary<object, (Type ConsumerType, IMiddlewarePipeline<ConsumeContext<string>> Pipeline)>
        {
            ["order.created"] = (typeof(ConsumerA), new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask)),
        };
        var invoker = CreateInvoker("test-router", ctx => (object?)"order.created", routedPipelines);
        var context = CreateContext("test-message");

        // Act & Assert — should not throw even with no Activity.Current
        await invoker.InvokeAsync(context);
    }

    [Fact]
    public async Task GivenNullRouteKey_WhenInvoking_ThenThrowsUnmatchedRouteException()
    {
        // Arrange
        var routedPipelines = new Dictionary<object, (Type ConsumerType, IMiddlewarePipeline<ConsumeContext<string>> Pipeline)>
        {
            ["key-a"] = (typeof(ConsumerA), new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask)),
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
        var routedPipelines = new Dictionary<object, (Type ConsumerType, IMiddlewarePipeline<ConsumeContext<string>> Pipeline)>
        {
            ["key-a"] = (typeof(ConsumerA), new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask)),
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
        var routedPipelines = new Dictionary<object, (Type ConsumerType, IMiddlewarePipeline<ConsumeContext<string>> Pipeline)>
        {
            ["key-a"] = (typeof(ConsumerA), new TestPipeline<ConsumeContext<string>>(ctx => { dispatchedA = true; return Task.CompletedTask; })),
            ["key-b"] = (typeof(ConsumerB), new TestPipeline<ConsumeContext<string>>(ctx => { dispatchedB = true; return Task.CompletedTask; })),
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
        Func<ConsumeContext<string>, object?> selector,
        Dictionary<object, (Type ConsumerType, IMiddlewarePipeline<ConsumeContext<string>> Pipeline)> routedPipelines)
    {
        return new MessageRouterInvoker<string>(
            identifier, selector, routedPipelines, logger);
    }

    private static TestInboundContext CreateContext(string message)
    {
        var services = new TestServiceProvider();
        return new TestInboundContext
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = services,
            Message = message,
            TransportContext = TestTransportContext.Create(services),
        };
    }

    private sealed class TestInboundContext : ConsumeContext<string>;

    private sealed class TestServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class ConsumerA : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class ConsumerB : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}

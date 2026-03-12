namespace Emit.Mediator.Tests;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using global::Emit.DependencyInjection;
using global::Emit.Mediator;
using global::Emit.Mediator.DependencyInjection;
using global::Emit.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class MediatorMiddlewareTests
{
    [Fact]
    public async Task GivenGlobalMiddleware_WhenSendAsync_ThenMiddlewareIsInvoked()
    {
        // Arrange
        var order = new List<string>();
        TrackingMiddleware<TestRequest>.Order = order;
        TestHandler.Tracker = order;

        var services = new ServiceCollection();
        services.AddEmit(emit =>
        {
            emit.InboundPipeline.Use(typeof(TrackingMiddleware<>));
            emit.AddMediator(m => m.AddHandler<TestHandler>());
        });
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new TestRequest("hello"));

        // Assert
        Assert.Equal("handled:hello", result);
        Assert.Equal(["tracking:before", "handler", "tracking:after"], order);
    }

    [Fact]
    public async Task GivenPatternMiddleware_WhenSendAsync_ThenMiddlewareIsInvoked()
    {
        // Arrange
        var order = new List<string>();
        TrackingMiddleware<TestRequest>.Order = order;
        TestHandler.Tracker = order;

        var services = new ServiceCollection();
        services.AddEmit(emit =>
        {
            emit.AddMediator(m =>
            {
                m.InboundPipeline.Use(typeof(TrackingMiddleware<>));
                m.AddHandler<TestHandler>();
            });
        });
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new TestRequest("hello"));

        // Assert
        Assert.Equal("handled:hello", result);
        Assert.Equal(["tracking:before", "handler", "tracking:after"], order);
    }

    [Fact]
    public async Task GivenGlobalAndPatternMiddleware_WhenSendAsync_ThenGlobalIsOutermost()
    {
        // Arrange
        var order = new List<string>();
        GlobalMiddleware<TestRequest>.Order = order;
        PatternMiddleware<TestRequest>.Order = order;
        TestHandler.Tracker = order;

        var services = new ServiceCollection();
        services.AddEmit(emit =>
        {
            emit.InboundPipeline.Use(typeof(GlobalMiddleware<>));
            emit.AddMediator(m =>
            {
                m.InboundPipeline.Use(typeof(PatternMiddleware<>));
                m.AddHandler<TestHandler>();
            });
        });
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new TestRequest("hello"));

        // Assert
        Assert.Equal("handled:hello", result);
        Assert.Equal(["global:before", "pattern:before", "handler", "pattern:after", "global:after"], order);
    }

    [Fact]
    public async Task GivenShortCircuitMiddleware_WhenSendAsync_ThenHandlerNotCalled()
    {
        // Arrange
        var handlerCalled = false;
        ShortCircuitHandler.WasCalled = () => handlerCalled = true;

        var services = new ServiceCollection();
        services.AddEmit(emit =>
        {
            emit.InboundPipeline.Use(typeof(ShortCircuitMiddleware<>));
            emit.AddMediator(m => m.AddHandler<ShortCircuitHandler>());
        });
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Act & Assert — void handler, no response expected
        await mediator.SendAsync(new ShortCircuitRequest());
        Assert.False(handlerCalled);
    }

    [Fact]
    public async Task GivenPerHandlerMiddleware_WhenSendAsync_ThenMiddlewareIsInvokedInCorrectOrder()
    {
        // Arrange
        var order = new List<string>();
        GlobalMiddleware<TestRequest>.Order = order;
        PatternMiddleware<TestRequest>.Order = order;
        HandlerMiddleware<TestRequest>.Order = order;
        TestHandler.Tracker = order;

        var services = new ServiceCollection();
        services.AddEmit(emit =>
        {
            emit.InboundPipeline.Use(typeof(GlobalMiddleware<>));
            emit.AddMediator(m =>
            {
                m.InboundPipeline.Use(typeof(PatternMiddleware<>));
                m.AddHandler<TestHandler, TestRequest>(handler =>
                {
                    handler.UseInbound(typeof(HandlerMiddleware<>));
                });
            });
        });
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new TestRequest("hello"));

        // Assert
        Assert.Equal("handled:hello", result);
        Assert.Equal(["global:before", "pattern:before", "handler:before", "handler", "handler:after", "pattern:after", "global:after"], order);
    }

    [Fact]
    public async Task GivenGlobalMiddleware_WhenSendVoidRequest_ThenMiddlewareIsInvoked()
    {
        // Arrange
        var order = new List<string>();
        TrackingMiddleware<VoidRequest>.Order = order;
        VoidHandler.Tracker = order;

        var services = new ServiceCollection();
        services.AddEmit(emit =>
        {
            emit.InboundPipeline.Use(typeof(TrackingMiddleware<>));
            emit.AddMediator(m => m.AddHandler<VoidHandler>());
        });
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Act
        await mediator.SendAsync(new VoidRequest());

        // Assert
        Assert.Equal(["tracking:before", "void-handler", "tracking:after"], order);
    }

    // ── Request/Response types ──

    private sealed record TestRequest(string Value) : IRequest<string>;
    private sealed record VoidRequest : IRequest;
    private sealed record ShortCircuitRequest : IRequest;

    // ── Handlers ──

    private sealed class TestHandler : IRequestHandler<TestRequest, string>
    {
        [ThreadStatic]
        internal static List<string>? Tracker;

        public Task<string> HandleAsync(TestRequest request, CancellationToken cancellationToken)
        {
            Tracker?.Add("handler");
            return Task.FromResult($"handled:{request.Value}");
        }
    }

    private sealed class VoidHandler : IRequestHandler<VoidRequest>
    {
        [ThreadStatic]
        internal static List<string>? Tracker;

        public Task HandleAsync(VoidRequest request, CancellationToken cancellationToken)
        {
            Tracker?.Add("void-handler");
            return Task.CompletedTask;
        }
    }

    private sealed class ShortCircuitHandler : IRequestHandler<ShortCircuitRequest>
    {
        [ThreadStatic]
        internal static Action? WasCalled;

        public Task HandleAsync(ShortCircuitRequest request, CancellationToken cancellationToken)
        {
            WasCalled?.Invoke();
            return Task.CompletedTask;
        }
    }

    // ── Middleware ──

    private sealed class TrackingMiddleware<T> : IMiddleware<MediatorContext<T>>
    {
        [ThreadStatic]
        internal static List<string>? Order;

        public async Task InvokeAsync(MediatorContext<T> context, IMiddlewarePipeline<MediatorContext<T>> next)
        {
            Order?.Add("tracking:before");
            await next.InvokeAsync(context).ConfigureAwait(false);
            Order?.Add("tracking:after");
        }
    }

    private sealed class GlobalMiddleware<T> : IMiddleware<MediatorContext<T>>
    {
        [ThreadStatic]
        internal static List<string>? Order;

        public async Task InvokeAsync(MediatorContext<T> context, IMiddlewarePipeline<MediatorContext<T>> next)
        {
            Order?.Add("global:before");
            await next.InvokeAsync(context).ConfigureAwait(false);
            Order?.Add("global:after");
        }
    }

    private sealed class PatternMiddleware<T> : IMiddleware<MediatorContext<T>>
    {
        [ThreadStatic]
        internal static List<string>? Order;

        public async Task InvokeAsync(MediatorContext<T> context, IMiddlewarePipeline<MediatorContext<T>> next)
        {
            Order?.Add("pattern:before");
            await next.InvokeAsync(context).ConfigureAwait(false);
            Order?.Add("pattern:after");
        }
    }

    private sealed class ShortCircuitMiddleware<T> : IMiddleware<MediatorContext<T>>
    {
        public Task InvokeAsync(MediatorContext<T> context, IMiddlewarePipeline<MediatorContext<T>> next) => Task.CompletedTask;
    }

    private sealed class HandlerMiddleware<T> : IMiddleware<MediatorContext<T>>
    {
        [ThreadStatic]
        internal static List<string>? Order;

        public async Task InvokeAsync(MediatorContext<T> context, IMiddlewarePipeline<MediatorContext<T>> next)
        {
            Order?.Add("handler:before");
            await next.InvokeAsync(context).ConfigureAwait(false);
            Order?.Add("handler:after");
        }
    }
}

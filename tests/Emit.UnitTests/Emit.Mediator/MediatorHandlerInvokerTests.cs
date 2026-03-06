namespace Emit.Mediator.Tests;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class MediatorHandlerInvokerTests
{
    [Fact]
    public async Task GivenRegisteredHandler_WhenInvoke_ThenResolvesHandlerAndCallsHandleAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<TestHandler>();
        var provider = services.BuildServiceProvider();

        var invoker = new MediatorHandlerInvoker<TestRequest, string>(typeof(TestHandler));
        var context = new InboundMediatorContext<TestRequest>
        {
            MessageId = "test-1",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = provider,
            Message = new TestRequest("hello"),
        };
        var responseFeature = new MediatorResponseFeature();
        context.Features.Set<IResponseFeature>(responseFeature);

        // Act
        await invoker.InvokeAsync(context);

        // Assert
        Assert.True(responseFeature.HasResponded);
        Assert.Equal("handled:hello", responseFeature.GetResponse<string>());
    }

    [Fact]
    public async Task GivenMissingResponseFeature_WhenInvoke_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<TestHandler>();
        var provider = services.BuildServiceProvider();

        var invoker = new MediatorHandlerInvoker<TestRequest, string>(typeof(TestHandler));
        var context = new InboundMediatorContext<TestRequest>
        {
            MessageId = "test-1",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = provider,
            Message = new TestRequest("hello"),
        };
        // No response feature set

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => invoker.InvokeAsync(context));
    }

    [Fact]
    public async Task GivenRegisteredVoidHandler_WhenInvoke_ThenResolvesHandlerAndCallsHandleAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<VoidTestHandler>();
        var provider = services.BuildServiceProvider();

        var invoker = new MediatorVoidHandlerInvoker<VoidTestRequest>(typeof(VoidTestHandler));
        var context = new InboundMediatorContext<VoidTestRequest>
        {
            MessageId = "test-1",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = provider,
            Message = new VoidTestRequest("hello"),
        };

        // Act
        await invoker.InvokeAsync(context);

        // Assert — no exception means handler was invoked successfully
    }

    private sealed record TestRequest(string Value) : IRequest<string>;
    private sealed record VoidTestRequest(string Value) : IRequest;

    private sealed class TestHandler : IRequestHandler<TestRequest, string>
    {
        public Task<string> HandleAsync(TestRequest request, CancellationToken cancellationToken)
            => Task.FromResult($"handled:{request.Value}");
    }

    private sealed class VoidTestHandler : IRequestHandler<VoidTestRequest>
    {
        public Task HandleAsync(VoidTestRequest request, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}

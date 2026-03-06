namespace Emit.Mediator.Tests;

using global::Emit.DependencyInjection;
using global::Emit.Mediator;
using global::Emit.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class MediatorTests
{
    [Fact]
    public async Task GivenRegisteredHandler_WhenSendAsync_ThenReturnsHandlerResponse()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddEmit(emit => emit.AddMediator(m => m.AddHandler<TestHandler>()));
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.SendAsync(new TestRequest("hello"));

        // Assert
        Assert.Equal("handled:hello", result);
    }

    [Fact]
    public async Task GivenUnregisteredRequestType_WhenSendAsync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddEmit(emit => emit.AddMediator(m => m.AddHandler<TestHandler>()));
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mediator.SendAsync(new UnregisteredRequest()));
    }

    [Fact]
    public async Task GivenHandlerThatThrows_WhenSendAsync_ThenExceptionPropagates()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddEmit(emit => emit.AddMediator(m => m.AddHandler<ThrowingHandler>()));
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => mediator.SendAsync(new ThrowingRequest()));
        Assert.Equal("handler failed", ex.Message);
    }

    [Fact]
    public async Task GivenNullRequest_WhenSendAsync_ThenThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddEmit(emit => emit.AddMediator(m => m.AddHandler<TestHandler>()));
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => mediator.SendAsync<string>(null!));
    }

    [Fact]
    public async Task GivenRegisteredVoidHandler_WhenSendAsync_ThenHandlerIsInvoked()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddEmit(emit => emit.AddMediator(m => m.AddHandler<VoidHandler>()));
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Act
        await mediator.SendAsync(new VoidRequest("test"));

        // Assert — handler ran without throwing
    }

    [Fact]
    public async Task GivenUnregisteredVoidRequestType_WhenSendAsync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddEmit(emit => emit.AddMediator(m => m.AddHandler<VoidHandler>()));
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mediator.SendAsync(new UnregisteredVoidRequest()));
    }

    [Fact]
    public async Task GivenNullVoidRequest_WhenSendAsync_ThenThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddEmit(emit => emit.AddMediator(m => m.AddHandler<VoidHandler>()));
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => mediator.SendAsync(null!));
    }

    private sealed record TestRequest(string Value) : IRequest<string>;
    private sealed record UnregisteredRequest : IRequest<string>;
    private sealed record ThrowingRequest : IRequest<string>;
    private sealed record VoidRequest(string Value) : IRequest;
    private sealed record UnregisteredVoidRequest : IRequest;

    private sealed class TestHandler : IRequestHandler<TestRequest, string>
    {
        public Task<string> HandleAsync(TestRequest request, CancellationToken cancellationToken)
            => Task.FromResult($"handled:{request.Value}");
    }

    private sealed class ThrowingHandler : IRequestHandler<ThrowingRequest, string>
    {
        public Task<string> HandleAsync(ThrowingRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("handler failed");
    }

    private sealed class VoidHandler : IRequestHandler<VoidRequest>
    {
        public Task HandleAsync(VoidRequest request, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}

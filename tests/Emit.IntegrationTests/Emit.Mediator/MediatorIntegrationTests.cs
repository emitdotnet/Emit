namespace Emit.Mediator.Tests;

using Emit.DependencyInjection;
using Emit.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

[Trait("Category", "Integration")]
public sealed class MediatorIntegrationTests
{
    [Fact]
    public async Task GivenRegisteredHandler_WhenSendAsync_ThenReturnsHandlerResponse()
    {
        // Arrange
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddEmit(emit =>
                    emit.AddMediator(m => m.AddHandler<GreetHandler>()));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var result = await mediator.SendAsync(new GreetRequest("world"));

            // Assert
            Assert.Equal("Hello, world!", result);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GivenHandlerThatThrows_WhenSendAsync_ThenExceptionPropagatesBackToCaller()
    {
        // Arrange
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddEmit(emit =>
                    emit.AddMediator(m => m.AddHandler<FailingHandler>()));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => mediator.SendAsync(new FailingRequest()));

            // Assert
            Assert.Equal("Something went wrong", ex.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ── Request / Response types ──

    private sealed record GreetRequest(string Name) : IRequest<string>;

    private sealed class GreetHandler : IRequestHandler<GreetRequest, string>
    {
        public Task<string> HandleAsync(GreetRequest request, CancellationToken cancellationToken)
            => Task.FromResult($"Hello, {request.Name}!");
    }

    // ── Failing types ──

    private sealed record FailingRequest : IRequest<string>;

    private sealed class FailingHandler : IRequestHandler<FailingRequest, string>
    {
        public Task<string> HandleAsync(FailingRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Something went wrong");
    }
}

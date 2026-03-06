namespace Emit.UnitTests.Abstractions.Pipeline;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using Xunit;

public sealed class MessagePipelineTests
{
    [Fact]
    public async Task GivenNoMiddleware_WhenBuild_ThenTerminalIsInvoked()
    {
        // Arrange
        var invoked = false;
        MessageDelegate<TestInboundContext> terminal = _ => { invoked = true; return Task.CompletedTask; };

        // Act
        var pipeline = MessagePipeline.Build(terminal, []);
        await pipeline(CreateContext());

        // Assert
        Assert.True(invoked);
    }

    [Fact]
    public async Task GivenSingleMiddleware_WhenBuild_ThenMiddlewareWrapsTerminal()
    {
        // Arrange
        var order = new List<string>();
        var middleware = new TrackingMiddleware("m1", order);
        MessageDelegate<TestInboundContext> terminal = _ => { order.Add("terminal"); return Task.CompletedTask; };

        // Act
        var pipeline = MessagePipeline.Build(terminal, [middleware]);
        await pipeline(CreateContext());

        // Assert
        Assert.Equal(["m1:before", "terminal", "m1:after"], order);
    }

    [Fact]
    public async Task GivenMultipleMiddleware_WhenBuild_ThenExecuteInListOrder()
    {
        // Arrange
        var order = new List<string>();
        var m1 = new TrackingMiddleware("m1", order);
        var m2 = new TrackingMiddleware("m2", order);
        var m3 = new TrackingMiddleware("m3", order);
        MessageDelegate<TestInboundContext> terminal = _ => { order.Add("terminal"); return Task.CompletedTask; };

        // Act
        var pipeline = MessagePipeline.Build(terminal, [m1, m2, m3]);
        await pipeline(CreateContext());

        // Assert
        Assert.Equal(["m1:before", "m2:before", "m3:before", "terminal", "m3:after", "m2:after", "m1:after"], order);
    }

    [Fact]
    public async Task GivenShortCircuitMiddleware_WhenBuild_ThenTerminalNotInvoked()
    {
        // Arrange
        var terminalInvoked = false;
        var shortCircuit = new ShortCircuitMiddleware();
        MessageDelegate<TestInboundContext> terminal = _ => { terminalInvoked = true; return Task.CompletedTask; };

        // Act
        var pipeline = MessagePipeline.Build(terminal, [shortCircuit]);
        await pipeline(CreateContext());

        // Assert
        Assert.False(terminalInvoked);
    }

    [Fact]
    public async Task GivenMiddlewareThatThrows_WhenBuild_ThenExceptionPropagates()
    {
        // Arrange
        var throwing = new ThrowingMiddleware();
        MessageDelegate<TestInboundContext> terminal = _ => Task.CompletedTask;

        // Act
        var pipeline = MessagePipeline.Build(terminal, [throwing]);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline(CreateContext()));
    }

    [Fact]
    public async Task GivenTerminalThatThrows_WhenMiddlewarePresent_ThenExceptionPropagatesThroughMiddleware()
    {
        // Arrange
        var order = new List<string>();
        var middleware = new TrackingMiddleware("m1", order);
        MessageDelegate<TestInboundContext> terminal = _ => throw new InvalidOperationException("terminal failed");

        // Act
        var pipeline = MessagePipeline.Build(terminal, [middleware]);

        // Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline(CreateContext()));
        Assert.Equal("terminal failed", ex.Message);
        Assert.Equal(["m1:before"], order);
    }

    [Fact]
    public void GivenNullTerminal_WhenBuild_ThenThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => MessagePipeline.Build<TestInboundContext>(null!, []));
    }

    [Fact]
    public void GivenNullMiddlewareList_WhenBuild_ThenThrowsArgumentNullException()
    {
        MessageDelegate<TestInboundContext> terminal = _ => Task.CompletedTask;
        Assert.Throws<ArgumentNullException>(() => MessagePipeline.Build(terminal, null!));
    }

    private static TestInboundContext CreateContext() => new()
    {
        MessageId = Guid.NewGuid().ToString(),
        Timestamp = DateTimeOffset.UtcNow,
        CancellationToken = CancellationToken.None,
        Services = null!,
        Message = "test",
    };

    private sealed class TestInboundContext : InboundContext<string>;

    private sealed class TrackingMiddleware(string name, List<string> order) : IMiddleware<TestInboundContext>
    {
        public async Task InvokeAsync(TestInboundContext context, MessageDelegate<TestInboundContext> next)
        {
            order.Add($"{name}:before");
            await next(context).ConfigureAwait(false);
            order.Add($"{name}:after");
        }
    }

    private sealed class ShortCircuitMiddleware : IMiddleware<TestInboundContext>
    {
        public Task InvokeAsync(TestInboundContext context, MessageDelegate<TestInboundContext> next) => Task.CompletedTask;
    }

    private sealed class ThrowingMiddleware : IMiddleware<TestInboundContext>
    {
        public Task InvokeAsync(TestInboundContext context, MessageDelegate<TestInboundContext> next)
            => throw new InvalidOperationException("middleware failed");
    }
}

namespace Emit.UnitTests.Pipeline;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Pipeline;
using Moq;
using Xunit;

public class ConsumerFilterMiddlewareTests
{
    [Fact]
    public async Task GivenPredicateReturnsTrue_WhenInvokeAsync_ThenCallsNext()
    {
        // Arrange
        var middleware = new ConsumerFilterMiddleware<string>((_, _) => ValueTask.FromResult(true));
        var context = CreateContext();
        var nextCalled = false;
        MessageDelegate<InboundContext<string>> next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task GivenPredicateReturnsFalse_WhenInvokeAsync_ThenSkipsNext()
    {
        // Arrange
        var middleware = new ConsumerFilterMiddleware<string>((_, _) => ValueTask.FromResult(false));
        var context = CreateContext();
        var nextCalled = false;
        MessageDelegate<InboundContext<string>> next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task GivenAsyncPredicate_WhenInvokeAsync_ThenAwaitsBeforeDeciding()
    {
        // Arrange
        var predicateAwaited = false;
        var middleware = new ConsumerFilterMiddleware<string>(async (_, ct) =>
        {
            await Task.Yield();
            predicateAwaited = true;
            return true;
        });
        var context = CreateContext();
        var nextCalled = false;
        MessageDelegate<InboundContext<string>> next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.True(predicateAwaited);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task GivenPredicate_WhenInvokeAsync_ThenReceivesContextAndCancellationToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var context = CreateContext(cancellationToken: cts.Token);
        InboundContext<string>? receivedContext = null;
        CancellationToken receivedToken = default;

        var middleware = new ConsumerFilterMiddleware<string>((ctx, ct) =>
        {
            receivedContext = ctx;
            receivedToken = ct;
            return ValueTask.FromResult(true);
        });

        MessageDelegate<InboundContext<string>> next = _ => Task.CompletedTask;

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.Same(context, receivedContext);
        Assert.Equal(cts.Token, receivedToken);
    }

    private static TestInboundContext<string> CreateContext(CancellationToken cancellationToken = default)
    {
        return new TestInboundContext<string>
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = cancellationToken,
            Services = Mock.Of<IServiceProvider>(),
            Message = "test-message"
        };
    }

    private sealed class TestInboundContext<T> : InboundContext<T>;
}

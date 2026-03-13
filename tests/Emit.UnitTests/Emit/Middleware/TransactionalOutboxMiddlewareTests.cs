namespace Emit.UnitTests.Middleware;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

public sealed class TransactionalOutboxMiddlewareTests
{
    // ── Test handler types ──

    [Transactional]
    private sealed class TransactionalTestHandler : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NonTransactionalTestHandler : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken ct) => Task.CompletedTask;
    }

    // ── Helpers ──

    private static ConsumeContext<string> CreateContext(
        IServiceProvider? services = null,
        CancellationToken cancellationToken = default)
    {
        var sp = services ?? new ServiceCollection().BuildServiceProvider();
        return new ConsumeContext<string>
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = cancellationToken,
            Services = sp,
            Message = "test-message",
            TransportContext = TestTransportContext.Create(sp),
        };
    }

    private static (IServiceProvider Services, Mock<IUnitOfWork> UnitOfWork, Mock<IUnitOfWorkTransaction> Transaction)
        CreateContextWithUnitOfWork()
    {
        var mockTransaction = new Mock<IUnitOfWorkTransaction>();
        mockTransaction.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mockTransaction.Setup(t => t.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var mockUow = new Mock<IUnitOfWork>();
        mockUow
            .Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockTransaction.Object);

        var services = new ServiceCollection();
        services.AddSingleton(mockUow.Object);
        var sp = services.BuildServiceProvider();

        return (sp, mockUow, mockTransaction);
    }

    // ── Tests ──

    [Fact]
    public async Task GivenHandlerWithTransactionalAttribute_WhenInvoked_ThenBeginsTransaction()
    {
        // Arrange
        var (sp, mockUow, _) = CreateContextWithUnitOfWork();
        var middleware = new TransactionalOutboxMiddleware<ConsumeContext<string>>(typeof(TransactionalTestHandler));
        var context = CreateContext(sp);
        var next = new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        mockUow.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenHandlerWithTransactionalAttribute_WhenInvokedSuccessfully_ThenCommitsTransaction()
    {
        // Arrange
        var (sp, _, mockTransaction) = CreateContextWithUnitOfWork();
        var middleware = new TransactionalOutboxMiddleware<ConsumeContext<string>>(typeof(TransactionalTestHandler));
        var context = CreateContext(sp);
        var next = new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        mockTransaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenHandlerWithTransactionalAttribute_WhenHandlerThrows_ThenTransactionIsDisposed()
    {
        // Arrange
        var (sp, _, mockTransaction) = CreateContextWithUnitOfWork();
        var middleware = new TransactionalOutboxMiddleware<ConsumeContext<string>>(typeof(TransactionalTestHandler));
        var context = CreateContext(sp);
        var next = new TestPipeline<ConsumeContext<string>>(_ => throw new InvalidOperationException("handler failed"));

        // Act — catch expected exception
        try
        {
            await middleware.InvokeAsync(context, next);
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        // Assert — transaction disposed (rollback-on-dispose semantics)
        mockTransaction.Verify(t => t.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task GivenHandlerWithTransactionalAttribute_WhenHandlerThrows_ThenExceptionPropagates()
    {
        // Arrange
        var (sp, _, _) = CreateContextWithUnitOfWork();
        var middleware = new TransactionalOutboxMiddleware<ConsumeContext<string>>(typeof(TransactionalTestHandler));
        var context = CreateContext(sp);
        var next = new TestPipeline<ConsumeContext<string>>(_ => throw new InvalidOperationException("handler failed"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(context, next));
    }

    [Fact]
    public async Task GivenHandlerWithoutTransactionalAttribute_WhenInvoked_ThenNoTransactionStarted()
    {
        // Arrange
        var (sp, mockUow, _) = CreateContextWithUnitOfWork();
        var middleware = new TransactionalOutboxMiddleware<ConsumeContext<string>>(typeof(NonTransactionalTestHandler));
        var context = CreateContext(sp);
        var next = new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        mockUow.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GivenHandlerWithTransactionalAttribute_WhenNextMiddlewareSucceeds_ThenNextIsCalled()
    {
        // Arrange
        var (sp, _, _) = CreateContextWithUnitOfWork();
        var middleware = new TransactionalOutboxMiddleware<ConsumeContext<string>>(typeof(TransactionalTestHandler));
        var context = CreateContext(sp);
        var nextCalled = false;
        var next = new TestPipeline<ConsumeContext<string>>(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task GivenHandlerWithTransactionalAttribute_WhenCancellationRequested_ThenCancellationTokenPropagated()
    {
        // Arrange
        var (sp, mockUow, _) = CreateContextWithUnitOfWork();
        using var cts = new CancellationTokenSource();
        var middleware = new TransactionalOutboxMiddleware<ConsumeContext<string>>(typeof(TransactionalTestHandler));
        var context = CreateContext(sp, cts.Token);
        var capturedToken = CancellationToken.None;
        var next = new TestPipeline<ConsumeContext<string>>(ctx =>
        {
            capturedToken = ctx.CancellationToken;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert — cancellation token passed to BeginAsync and propagated to next
        mockUow.Verify(u => u.BeginAsync(cts.Token), Times.Once);
        Assert.Equal(cts.Token, capturedToken);
    }
}

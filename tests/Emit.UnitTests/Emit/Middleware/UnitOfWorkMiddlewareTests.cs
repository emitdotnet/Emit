namespace Emit.UnitTests.Middleware;

using global::Emit.Abstractions;
using global::Emit.Abstractions.ErrorHandling;
using global::Emit.Abstractions.Metrics;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Consumer;
using global::Emit.Metrics;
using global::Emit.Middleware;
using global::Emit.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public sealed class UnitOfWorkMiddlewareTests
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

    private static UnitOfWorkMiddleware<ConsumeContext<string>> CreateMiddleware(Type handlerType) =>
        new(handlerType, NullLogger<UnitOfWorkMiddleware<ConsumeContext<string>>>.Instance);

    private static ConsumeContext<string> CreateContext(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        return new ConsumeContext<string>
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = cancellationToken,
            Services = services,
            Message = "test-message",
            TransportContext = TestTransportContext.Create(services),
        };
    }

    private static IServiceProvider CreateServices() =>
        new ServiceCollection()
            .AddScoped<IEmitContext, EmitContext>()
            .BuildServiceProvider();

    /// <summary>
    /// Services whose unit of work behaves like the real providers: beginning sets the scope's
    /// transaction, disposing clears it. Commits and disposals are appended to <paramref name="log"/>.
    /// </summary>
    private static (IServiceProvider Services, Mock<IUnitOfWork> UnitOfWork, Mock<IUnitOfWorkTransaction> Transaction)
        CreateServicesWithUnitOfWork(List<string>? log = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<IEmitContext, EmitContext>();

        var mockTransaction = new Mock<IUnitOfWorkTransaction>();
        var mockUow = new Mock<IUnitOfWork>();
        services.AddSingleton(mockUow.Object);
        var sp = services.BuildServiceProvider();

        var emitContext = sp.GetRequiredService<IEmitContext>();
        var transactionContext = new Mock<ITransactionContext>().Object;

        mockUow
            .Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
            .Callback(() => emitContext.Transaction = transactionContext)
            .ReturnsAsync(mockTransaction.Object);
        mockTransaction
            .Setup(t => t.CommitAsync(It.IsAny<CancellationToken>()))
            .Callback(() => log?.Add("commit"))
            .Returns(Task.CompletedTask);
        mockTransaction
            .Setup(t => t.DisposeAsync())
            .Callback(() =>
            {
                log?.Add("dispose");
                emitContext.Transaction = null;
            })
            .Returns(ValueTask.CompletedTask);

        return (sp, mockUow, mockTransaction);
    }

    // ── Transaction ──

    [Fact]
    public async Task GivenHandlerWithTransactionalAttribute_WhenInvoked_ThenBeginsTransaction()
    {
        // Arrange
        var (sp, mockUow, _) = CreateServicesWithUnitOfWork();
        var middleware = CreateMiddleware(typeof(TransactionalTestHandler));
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
        var (sp, _, mockTransaction) = CreateServicesWithUnitOfWork();
        var middleware = CreateMiddleware(typeof(TransactionalTestHandler));
        var context = CreateContext(sp);
        var next = new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        mockTransaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenHandlerWithTransactionalAttribute_WhenHandlerThrows_ThenTransactionIsDisposedAndExceptionPropagates()
    {
        // Arrange
        var (sp, _, mockTransaction) = CreateServicesWithUnitOfWork();
        var middleware = CreateMiddleware(typeof(TransactionalTestHandler));
        var context = CreateContext(sp);
        var next = new TestPipeline<ConsumeContext<string>>(_ => throw new InvalidOperationException("handler failed"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context, next));
        mockTransaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        mockTransaction.Verify(t => t.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task GivenHandlerWithoutTransactionalAttribute_WhenInvoked_ThenNoTransactionStartedAndNextCalled()
    {
        // Arrange
        var (sp, mockUow, _) = CreateServicesWithUnitOfWork();
        var middleware = CreateMiddleware(typeof(NonTransactionalTestHandler));
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
        mockUow.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GivenHandlerWithoutTransactionalAttribute_WhenNoUnitOfWorkRegistered_ThenRunsWithoutOne()
    {
        // Arrange
        var middleware = CreateMiddleware(typeof(NonTransactionalTestHandler));
        var context = CreateContext(CreateServices());
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
    public async Task GivenHandlerWithTransactionalAttribute_WhenInvoked_ThenCancellationTokenPropagated()
    {
        // Arrange
        var (sp, mockUow, _) = CreateServicesWithUnitOfWork();
        using var cts = new CancellationTokenSource();
        var middleware = CreateMiddleware(typeof(TransactionalTestHandler));
        var context = CreateContext(sp, cts.Token);
        var capturedToken = CancellationToken.None;
        var next = new TestPipeline<ConsumeContext<string>>(ctx =>
        {
            capturedToken = ctx.CancellationToken;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        mockUow.Verify(u => u.BeginAsync(cts.Token), Times.Once);
        Assert.Equal(cts.Token, capturedToken);
    }

    // ── Callbacks ──

    [Fact]
    public async Task GivenTransactionalHandlerWithCallbacks_WhenHandlerSucceeds_ThenSucceededRunsAfterCommitAndFailedNever()
    {
        // Arrange
        var log = new List<string>();
        var (sp, _, _) = CreateServicesWithUnitOfWork(log);
        var middleware = CreateMiddleware(typeof(TransactionalTestHandler));
        var context = CreateContext(sp);
        var next = new TestPipeline<ConsumeContext<string>>(ctx =>
        {
            var emitContext = ctx.Services.GetRequiredService<IEmitContext>();
            emitContext.OnSucceeded(_ => Log(log, "succeeded"));
            emitContext.OnFailed((_, _) => Log(log, "failed"));
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.Equal(["commit", "dispose", "succeeded"], log);
    }

    [Fact]
    public async Task GivenHandlerWithCallbacks_WhenHandlerThrowsAndNoRetryFollows_ThenFailedRunsWithTheException()
    {
        // Arrange
        var log = new List<string>();
        var (sp, _, _) = CreateServicesWithUnitOfWork(log);
        var middleware = CreateMiddleware(typeof(TransactionalTestHandler));
        var context = CreateContext(sp);
        var thrown = new InvalidOperationException("handler failed");
        Exception? received = null;
        var next = new TestPipeline<ConsumeContext<string>>(ctx =>
        {
            var emitContext = ctx.Services.GetRequiredService<IEmitContext>();
            emitContext.OnSucceeded(_ => Log(log, "succeeded"));
            emitContext.OnFailed((ex, _) =>
            {
                received = ex;
                return Log(log, "failed");
            });
            throw thrown;
        });

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context, next));

        // Assert
        Assert.Same(thrown, exception);
        Assert.Same(thrown, received);
        Assert.Equal(["dispose", "failed"], log);
    }

    [Fact]
    public async Task GivenHandlerWithCallbacks_WhenHandlerThrowsAndRetryFollows_ThenNoCallbackRuns()
    {
        // Arrange
        var log = new List<string>();
        var middleware = CreateMiddleware(typeof(NonTransactionalTestHandler));
        var context = CreateContext(CreateServices());
        context.WillRetry = _ => true;
        var next = new TestPipeline<ConsumeContext<string>>(ctx =>
        {
            var emitContext = ctx.Services.GetRequiredService<IEmitContext>();
            emitContext.OnSucceeded(_ => Log(log, "succeeded"));
            emitContext.OnFailed((_, _) => Log(log, "failed"));
            throw new InvalidOperationException("attempt failed");
        });

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context, next));

        // Assert
        Assert.Empty(log);
    }

    [Fact]
    public async Task GivenHandlerWithCallbacks_WhenCancelled_ThenFailedRunsWithTheCancellation()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var middleware = CreateMiddleware(typeof(NonTransactionalTestHandler));
        var context = CreateContext(CreateServices(), cts.Token);
        Exception? received = null;
        var next = new TestPipeline<ConsumeContext<string>>(ctx =>
        {
            ctx.Services.GetRequiredService<IEmitContext>().OnFailed((ex, _) =>
            {
                received = ex;
                return Task.CompletedTask;
            });
            cts.Cancel();
            ctx.CancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });

        // Act
        await Assert.ThrowsAsync<OperationCanceledException>(() => middleware.InvokeAsync(context, next));

        // Assert
        Assert.IsType<OperationCanceledException>(received);
    }

    [Fact]
    public async Task GivenSeveralCallbacks_WhenOneThrows_ThenTheRestRunInOrderAndOutcomeIsUnchanged()
    {
        // Arrange
        var log = new List<string>();
        var middleware = CreateMiddleware(typeof(NonTransactionalTestHandler));
        var context = CreateContext(CreateServices());
        var next = new TestPipeline<ConsumeContext<string>>(ctx =>
        {
            var emitContext = ctx.Services.GetRequiredService<IEmitContext>();
            emitContext.OnSucceeded(_ => Log(log, "first"));
            emitContext.OnSucceeded(_ => throw new InvalidOperationException("callback failed"));
            emitContext.OnSucceeded(_ => Log(log, "third"));
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.Equal(["first", "third"], log);
    }

    [Fact]
    public void GivenNoRunningUnitOfWork_WhenRegistering_ThenThrows()
    {
        // Arrange
        var emitContext = new EmitContext();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => emitContext.OnSucceeded(_ => Task.CompletedTask));
        Assert.Throws<InvalidOperationException>(() => emitContext.OnFailed((_, _) => Task.CompletedTask));
    }

    [Fact]
    public async Task GivenRunningCallback_WhenItRegistersAnother_ThenRegistrationThrows()
    {
        // Arrange
        var middleware = CreateMiddleware(typeof(NonTransactionalTestHandler));
        var context = CreateContext(CreateServices());
        Exception? registrationFailure = null;
        var next = new TestPipeline<ConsumeContext<string>>(ctx =>
        {
            var emitContext = ctx.Services.GetRequiredService<IEmitContext>();
            emitContext.OnSucceeded(_ =>
            {
                registrationFailure = Record.Exception(() => emitContext.OnSucceeded(_ => Task.CompletedTask));
                return Task.CompletedTask;
            });
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        Assert.IsType<InvalidOperationException>(registrationFailure);
    }

    [Fact]
    public async Task GivenWorkStartedByHandler_WhenItRegistersAfterTheUnitOfWorkEnded_ThenRegistrationThrows()
    {
        // Arrange
        var middleware = CreateMiddleware(typeof(NonTransactionalTestHandler));
        var context = CreateContext(CreateServices());
        var release = new TaskCompletionSource();
        Task<Exception?>? lateRegistration = null;
        var next = new TestPipeline<ConsumeContext<string>>(ctx =>
        {
            var emitContext = ctx.Services.GetRequiredService<IEmitContext>();
            lateRegistration = Task.Run<Exception?>(async () =>
            {
                await release.Task;
                return Record.Exception(() => emitContext.OnSucceeded(_ => Task.CompletedTask));
            });
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context, next);
        release.SetResult();

        // Assert
        Assert.IsType<InvalidOperationException>(await lateRegistration!);
    }

    [Fact]
    public async Task GivenNestedUnitOfWorkInsideEnclosingTransaction_WhenItSucceeds_ThenItsCallbackRunsAfterTheEnclosingCommit()
    {
        // Arrange
        var log = new List<string>();
        var (sp, _, _) = CreateServicesWithUnitOfWork(log);
        var outer = CreateMiddleware(typeof(TransactionalTestHandler));
        var inner = CreateMiddleware(typeof(NonTransactionalTestHandler));
        var context = CreateContext(sp);
        var innerHandler = new TestPipeline<ConsumeContext<string>>(ctx =>
        {
            ctx.Services.GetRequiredService<IEmitContext>().OnSucceeded(_ => Log(log, "inner succeeded"));
            return Task.CompletedTask;
        });
        var outerHandler = new TestPipeline<ConsumeContext<string>>(async ctx =>
        {
            await inner.InvokeAsync(CreateContext(ctx.Services), innerHandler);
            log.Add("inner returned");
        });

        // Act
        await outer.InvokeAsync(context, outerHandler);

        // Assert
        Assert.Equal(["inner returned", "commit", "dispose", "inner succeeded"], log);
    }

    [Fact]
    public async Task GivenNestedUnitOfWorkWithoutEnclosingTransaction_WhenItSucceeds_ThenItsCallbackRunsWhenItEnds()
    {
        // Arrange
        var log = new List<string>();
        var sp = CreateServices();
        var outer = CreateMiddleware(typeof(NonTransactionalTestHandler));
        var inner = CreateMiddleware(typeof(NonTransactionalTestHandler));
        var context = CreateContext(sp);
        var innerHandler = new TestPipeline<ConsumeContext<string>>(ctx =>
        {
            ctx.Services.GetRequiredService<IEmitContext>().OnSucceeded(_ => Log(log, "inner succeeded"));
            return Task.CompletedTask;
        });
        var outerHandler = new TestPipeline<ConsumeContext<string>>(async ctx =>
        {
            var emitContext = ctx.Services.GetRequiredService<IEmitContext>();
            emitContext.OnSucceeded(_ => Log(log, "outer succeeded"));
            await inner.InvokeAsync(CreateContext(ctx.Services), innerHandler);
            log.Add("inner returned");
        });

        // Act
        await outer.InvokeAsync(context, outerHandler);

        // Assert
        Assert.Equal(["inner succeeded", "inner returned", "outer succeeded"], log);
    }

    [Fact]
    public async Task GivenConcurrentUnitsOfWorkInOneScope_WhenOneFailsAndOneSucceeds_ThenEachRunsOnlyItsOwnCallbacks()
    {
        // Arrange
        var log = new List<string>();
        var sp = CreateServices();
        var bothRegistered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registered = 0;

        TestPipeline<ConsumeContext<string>> Handler(string name, bool fail) => new(async ctx =>
        {
            var emitContext = ctx.Services.GetRequiredService<IEmitContext>();
            emitContext.OnSucceeded(_ => Log(log, $"{name} succeeded"));
            emitContext.OnFailed((_, _) => Log(log, $"{name} failed"));
            if (Interlocked.Increment(ref registered) == 2)
            {
                bothRegistered.SetResult();
            }

            // Both register before either completes, so their registrations overlap in time.
            await bothRegistered.Task;
            if (fail)
            {
                throw new InvalidOperationException(name);
            }
        });

        var first = CreateMiddleware(typeof(NonTransactionalTestHandler))
            .InvokeAsync(CreateContext(sp), Handler("first", fail: false));
        var second = CreateMiddleware(typeof(NonTransactionalTestHandler))
            .InvokeAsync(CreateContext(sp), Handler("second", fail: true));

        // Act
        await first;
        await Assert.ThrowsAsync<InvalidOperationException>(() => second);

        // Assert
        Assert.Equal(["first succeeded", "second failed"], log.Order());
    }

    // ── With retry ──

    [Fact]
    public async Task GivenRetriedMessage_WhenALaterAttemptSucceeds_ThenOnlyThatAttemptsSucceededCallbackRuns()
    {
        // Arrange
        var log = new List<string>();
        var (context, pipeline) = CreateRetryingPipeline(maxAttempts: 2, attempt =>
        {
            if (attempt == 0)
            {
                throw new InvalidOperationException("first attempt failed");
            }
        }, log);

        // Act
        await pipeline.InvokeAsync(context);

        // Assert
        Assert.Equal(["succeeded on attempt 1"], log);
    }

    [Fact]
    public async Task GivenRetriedMessage_WhenEveryAttemptFails_ThenFailedRunsOnceForTheLastAttempt()
    {
        // Arrange
        var log = new List<string>();
        var (context, pipeline) = CreateRetryingPipeline(maxAttempts: 2, _ =>
            throw new InvalidOperationException("attempt failed"), log);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.InvokeAsync(context));

        // Assert
        Assert.Equal(["failed on attempt 2"], log);
    }

    private static (ConsumeContext<string> Context, IMiddlewarePipeline<ConsumeContext<string>> Pipeline)
        CreateRetryingPipeline(int maxAttempts, Action<int> handle, List<string> log)
    {
        var sp = CreateServices();
        var handler = new TestPipeline<ConsumeContext<string>>(ctx =>
        {
            var attempt = ctx.RetryAttempt;
            var emitContext = ctx.Services.GetRequiredService<IEmitContext>();
            emitContext.OnSucceeded(_ => Log(log, $"succeeded on attempt {attempt}"));
            emitContext.OnFailed((_, _) => Log(log, $"failed on attempt {attempt}"));
            handle(attempt);
            return Task.CompletedTask;
        });

        var retry = new RetryMiddleware<string>(
            _ => ErrorAction.Retry(maxAttempts, Backoff.None, ErrorAction.DeadLetter()),
            new EmitMetrics(null, new EmitMetricsEnrichment()),
            new EmitTracingOptions(),
            new Mock<INodeIdentity>().Object,
            NullLogger<RetryMiddleware<string>>.Instance);

        var pipeline = new TestPipeline<ConsumeContext<string>>(ctx =>
            retry.InvokeAsync(ctx, new TestPipeline<ConsumeContext<string>>(attemptContext =>
                CreateMiddleware(typeof(NonTransactionalTestHandler)).InvokeAsync(attemptContext, handler))));

        return (CreateContext(sp), pipeline);
    }

    private static Task Log(List<string> log, string entry)
    {
        lock (log)
        {
            log.Add(entry);
        }

        return Task.CompletedTask;
    }
}

namespace Emit.UnitTests.Consumer;

using global::Emit.Abstractions;
using global::Emit.Abstractions.ErrorHandling;
using global::Emit.Abstractions.Metrics;
using global::Emit.Consumer;
using global::Emit.Metrics;
using global::Emit.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public sealed class RetryMiddlewareTests
{
    private static readonly EmitMetrics Metrics = new(null, new EmitMetricsEnrichment());
    private static readonly Mock<INodeIdentity> NodeIdentity = new();

    // ── Helpers ──

    private static RetryMiddleware<string> CreateMiddleware(int maxAttempts) =>
        CreateMiddleware(_ => ErrorAction.Retry(maxAttempts, Backoff.None, ErrorAction.DeadLetter()));

    private static RetryMiddleware<string> CreateMiddleware(Func<Exception, ErrorAction> policy) =>
        new(policy, Metrics, new EmitTracingOptions(), NodeIdentity.Object, NullLogger<RetryMiddleware<string>>.Instance);

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

    private static IServiceProvider BuildServiceProviderWithScopeTracking(
        out List<IServiceProvider> scopeServiceProviders,
        out List<bool> scopeDisposed)
    {
        var trackedScopes = new List<IServiceProvider>();
        var disposedFlags = new List<bool>();
        scopeServiceProviders = trackedScopes;
        scopeDisposed = disposedFlags;

        var mockRootSp = new Mock<IServiceProvider>();
        var mockScopeFactory = new Mock<IServiceScopeFactory>();

        mockRootSp
            .Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(mockScopeFactory.Object);

        mockScopeFactory
            .Setup(f => f.CreateScope())
            .Returns(() =>
            {
                var index = trackedScopes.Count;
                disposedFlags.Add(false);

                var scopeSp = new ServiceCollection().BuildServiceProvider();
                trackedScopes.Add(scopeSp);

                var mockScope = new Mock<IServiceScope>();
                mockScope.SetupGet(s => s.ServiceProvider).Returns(scopeSp);
                mockScope.Setup(s => s.Dispose()).Callback(() => disposedFlags[index] = true);
                return mockScope.Object;
            });

        return mockRootSp.Object;
    }

    // ── Tests ──

    [Fact]
    public async Task GivenRetryMiddleware_WhenRetryOccurs_ThenNewScopeCreatedPerAttempt()
    {
        // Arrange
        var middleware = CreateMiddleware(maxAttempts: 2);
        var sp = BuildServiceProviderWithScopeTracking(
            out var scopeServiceProviders,
            out _);
        var context = CreateContext(sp);

        var callCount = 0;
        Func<Task> next = () =>
        {
            callCount++;
            if (callCount == 1)
                throw new InvalidOperationException("first attempt fails");
            return Task.CompletedTask;
        };

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert — one new scope was created for the retry attempt
        Assert.Single(scopeServiceProviders);
    }

    [Fact]
    public async Task GivenRetryMiddleware_WhenRetryOccurs_ThenScopedContextHasDifferentServices()
    {
        // Arrange
        var middleware = CreateMiddleware(maxAttempts: 2);
        var sp = BuildServiceProviderWithScopeTracking(
            out var scopeServiceProviders,
            out _);
        var context = CreateContext(sp);

        var seenServiceProviders = new List<IServiceProvider>();
        var callCount = 0;
        Func<Task> next = () =>
        {
            seenServiceProviders.Add(context.Services);
            callCount++;
            if (callCount == 1)
                throw new InvalidOperationException("first attempt fails");
            return Task.CompletedTask;
        };

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert — the retry invocation sees a different service provider than the original
        Assert.Equal(2, seenServiceProviders.Count);
        Assert.NotSame(sp, seenServiceProviders[1]);
        Assert.Same(scopeServiceProviders[0], seenServiceProviders[1]);
    }

    [Fact]
    public async Task GivenRetryMiddleware_WhenRetrySucceeds_ThenRetryScopeIsDisposed()
    {
        // Arrange
        var middleware = CreateMiddleware(maxAttempts: 2);
        var sp = BuildServiceProviderWithScopeTracking(
            out _,
            out var scopeDisposed);
        var context = CreateContext(sp);

        var callCount = 0;
        Func<Task> next = () =>
        {
            callCount++;
            if (callCount == 1)
                throw new InvalidOperationException("first attempt fails");
            return Task.CompletedTask;
        };

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert — the retry scope was disposed after the retry completes
        Assert.Single(scopeDisposed);
        Assert.True(scopeDisposed[0]);
    }

    [Fact]
    public async Task GivenRetryMiddleware_WhenHandlerSucceedsOnFirstTry_ThenNoScopeCreated()
    {
        // Arrange
        var middleware = CreateMiddleware(maxAttempts: 2);
        var sp = BuildServiceProviderWithScopeTracking(
            out var scopeServiceProviders,
            out _);
        var context = CreateContext(sp);

        Func<Task> next = () => Task.CompletedTask;

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert — no retry scope was created since the first attempt succeeded
        Assert.Empty(scopeServiceProviders);
    }

    [Fact]
    public async Task GivenRetryMiddleware_WhenAllAttemptsExhausted_ThenScopeCreatedPerAttempt()
    {
        // Arrange
        var middleware = CreateMiddleware(maxAttempts: 3);
        var sp = BuildServiceProviderWithScopeTracking(
            out var scopeServiceProviders,
            out _);
        var context = CreateContext(sp);

        Func<Task> next = () =>
            throw new InvalidOperationException("always fails");

        // Act & Assert — exception propagates after all retries
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(context, next));

        // One scope per retry attempt (3 retries)
        Assert.Equal(3, scopeServiceProviders.Count);
    }

    [Fact]
    public async Task GivenClauseThatDoesNotRetry_WhenItsExceptionIsThrown_ThenNotRetried()
    {
        // Arrange
        var middleware = CreateMiddleware(ex => ex is ArgumentException
            ? ErrorAction.DeadLetter()
            : ErrorAction.Retry(3, Backoff.None, ErrorAction.DeadLetter()));
        var context = CreateContext(new ServiceCollection().BuildServiceProvider());
        var calls = 0;
        Func<Task> next = () =>
        {
            calls++;
            throw new ArgumentException("not retryable");
        };

        // Act
        await Assert.ThrowsAsync<ArgumentException>(() => middleware.InvokeAsync(context, next));

        // Assert
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GivenClauseThatRetries_WhenOnlyThatClauseRetries_ThenItsAttemptsAreUsed()
    {
        // Arrange
        var middleware = CreateMiddleware(ex => ex is TimeoutException
            ? ErrorAction.Retry(2, Backoff.None, ErrorAction.DeadLetter())
            : ErrorAction.DeadLetter());
        var context = CreateContext(new ServiceCollection().BuildServiceProvider());
        var calls = 0;
        Func<Task> next = () =>
        {
            calls++;
            throw new TimeoutException("transient");
        };

        // Act
        await Assert.ThrowsAsync<TimeoutException>(() => middleware.InvokeAsync(context, next));

        // Assert: the first attempt plus two retries
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task GivenDifferentExceptionsAcrossAttempts_WhenALaterOneIsNotRetryable_ThenRetryStopsAtIt()
    {
        // Arrange
        var middleware = CreateMiddleware(ex => ex is TimeoutException
            ? ErrorAction.Retry(5, Backoff.None, ErrorAction.DeadLetter())
            : ErrorAction.Discard());
        var context = CreateContext(new ServiceCollection().BuildServiceProvider());
        var calls = 0;
        Func<Task> next = () =>
        {
            calls++;
            throw calls == 1
                ? new TimeoutException("transient")
                : new ArgumentException("permanent");
        };

        // Act
        await Assert.ThrowsAsync<ArgumentException>(() => middleware.InvokeAsync(context, next));

        // Assert
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GivenRetryMiddleware_WhenAnAttemptFails_ThenWillRetryMatchesWhetherAnotherAttemptRuns()
    {
        // Arrange
        var middleware = CreateMiddleware(maxAttempts: 1);
        var context = CreateContext(new ServiceCollection().BuildServiceProvider());
        var decisions = new List<bool>();
        Func<Task> next = () =>
        {
            var exception = new InvalidOperationException("attempt failed");
            decisions.Add(context.WillRetry!(exception));
            throw exception;
        };

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context, next));

        // Assert: the first attempt is retried, the single retry is not
        Assert.Equal([true, false], decisions);
    }

    [Fact]
    public async Task GivenShutdown_WhenAttemptIsCancelled_ThenNotRetried()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var middleware = CreateMiddleware(maxAttempts: 3);
        var context = CreateContext(new ServiceCollection().BuildServiceProvider(), cts.Token);
        var calls = 0;
        bool? willRetry = null;
        Func<Task> next = () =>
        {
            calls++;
            cts.Cancel();
            var exception = new OperationCanceledException(context.CancellationToken);
            willRetry = context.WillRetry!(exception);
            throw exception;
        };

        // Act
        await Assert.ThrowsAsync<OperationCanceledException>(() => middleware.InvokeAsync(context, next));

        // Assert
        Assert.Equal(1, calls);
        Assert.False(willRetry);
    }

    [Fact]
    public async Task GivenRetryHeldByTheCircuitBreaker_WhenItWaits_ThenItSpendsNoAttemptUntilItRuns()
    {
        // Arrange: the circuit holds the second attempt until released
        var heldAtGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateCalls = 0;
        var breaker = new Mock<ICircuitBreakerNotifier>();
        breaker
            .Setup(b => b.BeginAttemptAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                if (Interlocked.Increment(ref gateCalls) == 2)
                {
                    heldAtGate.TrySetResult();
                    await release.Task;
                }

                return Mock.Of<IAsyncDisposable>();
            });
        var middleware = new RetryMiddleware<string>(
            _ => ErrorAction.Retry(3, Backoff.None, ErrorAction.DeadLetter()),
            Metrics,
            new EmitTracingOptions(),
            NodeIdentity.Object,
            NullLogger<RetryMiddleware<string>>.Instance,
            breaker.Object);
        var context = CreateContext(new ServiceCollection().BuildServiceProvider());
        List<int> attempts = [];
        Func<Task> next = () =>
        {
            attempts.Add(context.RetryAttempt);
            return attempts.Count == 1 ? throw new InvalidOperationException("first attempt fails") : Task.CompletedTask;
        };

        // Act
        var run = middleware.InvokeAsync(context, next);
        await heldAtGate.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var attemptWhileHeld = context.RetryAttempt;
        release.SetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.Equal(0, attemptWhileHeld);
        Assert.Equal([0, 1], attempts);
    }
}

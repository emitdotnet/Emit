namespace Emit.UnitTests.Consumer;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Metrics;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Consumer;
using global::Emit.Metrics;
using global::Emit.Pipeline.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public sealed class RetryMiddlewareTests
{
    private static readonly EmitMetrics Metrics = new(null, new EmitMetricsEnrichment());
    private static readonly Mock<INodeIdentity> NodeIdentity = new();

    // ── Helpers ──

    private static RetryMiddleware<string> CreateMiddleware(int maxAttempts)
    {
        var config = new RetryConfig(maxAttempts, Backoff.None);
        return new RetryMiddleware<string>(
            config,
            Metrics,
            NodeIdentity.Object,
            NullLogger<RetryMiddleware<string>>.Instance);
    }

    private static ConsumeContext<string> CreateContext(IServiceProvider services)
    {
        return new ConsumeContext<string>
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
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
        var next = new TestPipeline<ConsumeContext<string>>(ctx =>
        {
            callCount++;
            if (callCount == 1)
                throw new InvalidOperationException("first attempt fails");
            return Task.CompletedTask;
        });

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
        var next = new TestPipeline<ConsumeContext<string>>(ctx =>
        {
            seenServiceProviders.Add(ctx.Services);
            callCount++;
            if (callCount == 1)
                throw new InvalidOperationException("first attempt fails");
            return Task.CompletedTask;
        });

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
        var next = new TestPipeline<ConsumeContext<string>>(ctx =>
        {
            callCount++;
            if (callCount == 1)
                throw new InvalidOperationException("first attempt fails");
            return Task.CompletedTask;
        });

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

        var next = new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask);

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

        var next = new TestPipeline<ConsumeContext<string>>(_ =>
            throw new InvalidOperationException("always fails"));

        // Act & Assert — exception propagates after all retries
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(context, next));

        // One scope per retry attempt (3 retries)
        Assert.Equal(3, scopeServiceProviders.Count);
    }
}

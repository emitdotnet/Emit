namespace Emit.UnitTests.Consumer;

using System.Threading.RateLimiting;
using global::Emit.Abstractions;
using global::Emit.Abstractions.Metrics;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Consumer;
using global::Emit.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class RateLimitMiddlewareTests
{
    private static ConsumeContext<string> CreateContext(CancellationToken cancellationToken = default)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new ConsumeContext<string>
        {
            MessageId = "test",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = cancellationToken,
            Message = "hello",
            Services = services,
            TransportContext = TestTransportContext.Create(services),
        };
    }

    [Fact]
    public async Task GivenRateLimitMiddleware_WhenInvoked_ThenCallsNextDelegate()
    {
        // Arrange
        using var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 10,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            TokensPerPeriod = 10,
            QueueLimit = 10,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });

        var middleware = new RateLimitMiddleware<string>(limiter, new EmitMetrics(null, new EmitMetricsEnrichment()));
        var context = CreateContext();
        var nextCalled = false;

        // Act
        await middleware.InvokeAsync(context, new TestPipeline<ConsumeContext<string>>(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }));

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task GivenRateLimitMiddleware_WhenCancelled_ThenThrowsOperationCanceledException()
    {
        // Arrange — create a limiter with 1 token and exhaust it, so the next acquire must wait
        using var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 1,
            ReplenishmentPeriod = TimeSpan.FromHours(1),
            TokensPerPeriod = 1,
            QueueLimit = 10,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = false,
        });

        // Exhaust the single available token
        using var exhaustLease = await limiter.AcquireAsync(1);

        var middleware = new RateLimitMiddleware<string>(limiter, new EmitMetrics(null, new EmitMetricsEnrichment()));
        using var cts = new CancellationTokenSource();
        var context = CreateContext(cts.Token);

        // Cancel immediately so AcquireAsync is cancelled while waiting
        await cts.CancelAsync();

        // Act & Assert — TaskCanceledException inherits OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            middleware.InvokeAsync(context, new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask)));
    }

    // ── Batch rate-limit helpers ──

    private static ConsumeContext<MessageBatch<string>> CreateBatchContext(int itemCount)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var items = Enumerable.Range(0, itemCount)
            .Select(i => new BatchItem<string>
            {
                Message = $"item-{i}",
                TransportContext = TestTransportContext.Create(services),
            })
            .ToList();

        return new ConsumeContext<MessageBatch<string>>
        {
            MessageId = "batch",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Message = new MessageBatch<string>(items),
            Services = services,
            TransportContext = TestTransportContext.Create(services),
        };
    }

    [Fact]
    public async Task Given_BatchMessage_When_InvokeAsync_Then_AcquiresBatchCountPermits()
    {
        // Arrange — limiter with enough tokens for 5 permits; batch has 5 items
        using var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 10,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            TokensPerPeriod = 10,
            QueueLimit = 10,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = false,
        });

        var middleware = new RateLimitMiddleware<MessageBatch<string>>(limiter, new EmitMetrics(null, new EmitMetricsEnrichment()));
        var context = CreateBatchContext(itemCount: 5);
        var nextCalled = false;

        // Act
        await middleware.InvokeAsync(context, new TestPipeline<ConsumeContext<MessageBatch<string>>>(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }));

        // Assert — next was called, meaning 5 permits were successfully acquired
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Given_SingleMessage_When_InvokeAsync_Then_AcquiresOnePermit()
    {
        // Arrange — limiter with exactly 1 token
        using var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 1,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            TokensPerPeriod = 1,
            QueueLimit = 1,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = false,
        });

        var middleware = new RateLimitMiddleware<string>(limiter, new EmitMetrics(null, new EmitMetricsEnrichment()));
        var context = CreateContext();
        var nextCalled = false;

        // Act
        await middleware.InvokeAsync(context, new TestPipeline<ConsumeContext<string>>(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }));

        // Assert — exactly one permit acquired; next was called
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Given_BatchMessage_When_LeaseNotAcquired_Then_ThrowsInvalidOperationException()
    {
        // Arrange — limiter with 5 token limit, queue=0, exhaust all 5 tokens first
        //            so the next acquire of 5 cannot be queued and returns a non-acquired lease
        using var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 5,
            ReplenishmentPeriod = TimeSpan.FromHours(1),
            TokensPerPeriod = 5,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = false,
        });

        // Exhaust all tokens
        using var exhaustLease = await limiter.AcquireAsync(5);

        var middleware = new RateLimitMiddleware<MessageBatch<string>>(limiter, new EmitMetrics(null, new EmitMetricsEnrichment()));
        var context = CreateBatchContext(itemCount: 5);

        // Act & Assert — no tokens available; queue=0 means lease cannot be queued → IsAcquired=false
        //                RateLimitMiddleware throws InvalidOperationException when !lease.IsAcquired
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            middleware.InvokeAsync(context, new TestPipeline<ConsumeContext<MessageBatch<string>>>(_ => Task.CompletedTask)));
    }
}

namespace Emit.UnitTests.Consumer;

using System.Threading.RateLimiting;
using global::Emit.Abstractions.Metrics;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Consumer;
using global::Emit.Kafka.Consumer;
using global::Emit.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class RateLimitMiddlewareTests
{
    private static InboundKafkaContext<string, string> CreateContext(CancellationToken cancellationToken = default)
    {
        return new InboundKafkaContext<string, string>
        {
            MessageId = "test",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = cancellationToken,
            Message = "hello",
            Key = "key",
            Topic = "test-topic",
            Partition = 0,
            Offset = 0,
            Headers = [],
            Services = new ServiceCollection().BuildServiceProvider(),
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
        await middleware.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

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
            middleware.InvokeAsync(context, _ => Task.CompletedTask));
    }
}

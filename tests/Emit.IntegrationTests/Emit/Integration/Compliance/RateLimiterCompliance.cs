namespace Emit.IntegrationTests.Integration.Compliance;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for consumer-side rate limiting. Derived classes configure a
/// <c>string, string</c> topic with a rate-limited consumer group that uses a single worker,
/// so that throughput is deterministically bounded by the configured rate limit.
/// </summary>
[Trait("Category", "Integration")]
public abstract class RateLimiterCompliance
{
    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic whose consumer group
    /// applies a rate limit of <paramref name="permitsPerWindow"/> permits per <paramref name="window"/>.
    /// The consumer group must use a single worker and be backed by <see cref="SinkConsumer{T}"/>
    /// of <see cref="string"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    /// <param name="permitsPerWindow">The number of permits allowed per rate-limit window.</param>
    /// <param name="window">The duration of each rate-limit window.</param>
    protected abstract void ConfigureWithRateLimit(
        EmitBuilder emit,
        string topic,
        string groupId,
        int permitsPerWindow,
        TimeSpan window);

    /// <summary>
    /// Verifies that a consumer group respects the configured rate limit by checking that
    /// processing a batch of messages that spans multiple windows takes at least as long
    /// as the rate limit implies.
    /// </summary>
    [Fact]
    public async Task GivenRateLimitedConsumer_WhenMessagesExceedPermits_ThenThroughputThrottled()
    {
        // Arrange — 18 messages, 3 permits per 1-second window → requires at least 6 windows (≥ 5s).
        // Deliberately over-provisioned so that even under heavy parallel test load, a genuine
        // rate limiter will always produce elapsed time well above the minimum.
        const int messageCount = 18;
        const int permitsPerWindow = 3;
        var window = TimeSpan.FromSeconds(1);

        var topic = $"test-rl-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureWithRateLimit(emit, topic, groupId, permitsPerWindow, window));
            })
            .Build();

        await host.StartAsync();

        try
        {
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();

            // Act — start the clock before producing so any consumption that happens during
            // production is included in the measured elapsed time.
            var sw = Stopwatch.StartNew();

            for (var i = 0; i < messageCount; i++)
            {
                await producer.ProduceAsync(new EventMessage<string, string>("k", $"msg-{i}"));
            }

            // Collect all messages.
            for (var i = 0; i < messageCount; i++)
            {
                await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            }

            sw.Stop();

            // Assert — processing all 9 messages across 3 permits/window must span ≥ 2 windows.
            var minExpectedElapsed = window * (messageCount / permitsPerWindow - 1);
            Assert.True(
                sw.Elapsed >= minExpectedElapsed,
                $"Expected elapsed >= {minExpectedElapsed} but got {sw.Elapsed}. Rate limiting did not throttle throughput.");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}

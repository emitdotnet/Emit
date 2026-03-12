namespace Emit.IntegrationTests.Integration.Compliance;

using System.Collections.Concurrent;
using Emit.Abstractions;
using Emit.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for content-based message routing. Derived classes configure a
/// <c>string, string</c> topic with a router whose selector returns the message value
/// as the route key. Routes "route-a" and "route-b" dispatch to <see cref="ConsumerA"/>
/// and <see cref="ConsumerB"/> respectively. Unmatched routes are discarded.
/// </summary>
[Trait("Category", "Integration")]
public abstract class RoutingCompliance
{
    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer
    /// and a consumer group containing a router with routes "route-a" and "route-b".
    /// The selector must use the message value as the route key.
    /// Unmatched routes must be discarded via <c>WhenRouteUnmatched(d =&gt; d.Discard())</c>.
    /// <see cref="ConsumerA"/> and <see cref="ConsumerB"/> write their prefixed output to
    /// the shared <see cref="RoutingCapture"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    protected abstract void ConfigureWithRouter(EmitBuilder emit, string topic, string groupId);

    /// <summary>
    /// Verifies that a message matching a registered route is dispatched to the correct consumer.
    /// </summary>
    [Fact]
    public async Task GivenMatchingRoute_WhenMessageProduced_ThenRoutedToCorrectConsumer()
    {
        // Arrange
        var topic = $"test-route-match-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var capture = new RoutingCapture();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(capture);
                services.AddEmit(emit => ConfigureWithRouter(emit, topic, groupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce "route-a" (routed to ConsumerA) and "route-b" (routed to ConsumerB).
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "route-a"));
            await producer.ProduceAsync(new EventMessage<string, string>("k", "route-b"));

            // Assert — wait for both messages to arrive, then verify prefixes.
            await capture.WaitForCountAsync(2, TimeSpan.FromSeconds(30));
            var received = capture.Messages.ToHashSet();
            Assert.Contains("A:route-a", received);
            Assert.Contains("B:route-b", received);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer
    /// and a consumer group containing two routers: one routing "router-one-a" to <see cref="ConsumerA"/>
    /// and one routing "router-two-b" to <see cref="ConsumerB"/>. Both routers use the message value
    /// as the route key. Unmatched routes must be discarded.
    /// <see cref="ConsumerA"/> and <see cref="ConsumerB"/> write their prefixed output to
    /// the shared <see cref="RoutingCapture"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    protected abstract void ConfigureWithTwoRouters(EmitBuilder emit, string topic, string groupId);

    /// <summary>
    /// Verifies that when two routers are registered on the same consumer group, each router
    /// independently dispatches matching messages to its registered handler.
    /// </summary>
    [Fact]
    public async Task GivenTwoRoutersOnSameGroup_WhenMessagesProduced_ThenEachRouterDispatchesToItsConsumer()
    {
        // Arrange
        var topic = $"test-route-two-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var capture = new RoutingCapture();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(capture);
                services.AddEmit(emit => ConfigureWithTwoRouters(emit, topic, groupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce one message matched by the first router and one by the second.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "router-one-a"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "router-two-b"));

            // Assert — both messages arrive via their respective routers.
            await capture.WaitForCountAsync(2, TimeSpan.FromSeconds(30));
            var received = capture.Messages.ToHashSet();
            Assert.Contains("A:router-one-a", received);
            Assert.Contains("B:router-two-b", received);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that a message with no matching route is discarded and not delivered to any consumer.
    /// </summary>
    [Fact]
    public async Task GivenUnmatchedRoute_WhenMessageProduced_ThenDiscarded()
    {
        // Arrange
        var topic = $"test-route-unmatched-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var capture = new RoutingCapture();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(capture);
                services.AddEmit(emit => ConfigureWithRouter(emit, topic, groupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce "unmatched" (no route), then "route-a" to confirm consumer is alive.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "unmatched"));
            await producer.ProduceAsync(new EventMessage<string, string>("k", "route-a"));

            // Assert — only the sentinel "A:route-a" arrives; "unmatched" was discarded.
            await capture.WaitForCountAsync(1, TimeSpan.FromSeconds(30));
            Assert.Equal("A:route-a", capture.Messages.Single());
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Collects routing output from <see cref="ConsumerA"/> and <see cref="ConsumerB"/>.
    /// Thread-safe; supports async waiting for a specific message count.
    /// </summary>
    public sealed class RoutingCapture
    {
        private readonly ConcurrentQueue<string> messages = new();
        private readonly SemaphoreSlim signal = new(0);

        /// <summary>Gets all captured messages in arrival order.</summary>
        public IReadOnlyCollection<string> Messages => messages;

        /// <summary>Adds a message and signals waiting observers.</summary>
        public void Add(string message)
        {
            messages.Enqueue(message);
            signal.Release();
        }

        /// <summary>Waits until at least <paramref name="count"/> messages have been captured.</summary>
        public async Task WaitForCountAsync(int count, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            for (var i = 0; i < count; i++)
            {
                await signal.WaitAsync(cts.Token);
            }
        }
    }

    /// <summary>
    /// Consumer A for routing tests. Writes <c>"A:" + message</c> to the shared <see cref="RoutingCapture"/>.
    /// </summary>
    public sealed class ConsumerA(RoutingCapture capture) : IConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
        {
            capture.Add("A:" + context.Message);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Consumer B for routing tests. Writes <c>"B:" + message</c> to the shared <see cref="RoutingCapture"/>.
    /// </summary>
    public sealed class ConsumerB(RoutingCapture capture) : IConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
        {
            capture.Add("B:" + context.Message);
            return Task.CompletedTask;
        }
    }
}

namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.Consumer;
using Emit.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for rebalances: a handler still running when its partition is revoked is cancelled before the
/// partition is handed over, so a key is never handled by two consumers at once. Derived classes configure a
/// provider-specific producer and consumer group for a <c>string, string</c> topic containing
/// <see cref="TrackingConsumer"/>.
/// </summary>
[Trait("Category", "Integration")]
public abstract class RebalanceCompliance
{
    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer and a consumer group
    /// <paramref name="groupId"/> containing <see cref="TrackingConsumer"/>, using the given
    /// <paramref name="workerStopTimeout"/>. Called once per host, every host joining the same group.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group every host joins.</param>
    /// <param name="workerStopTimeout">Maximum time to drain workers on pool shutdown.</param>
    protected abstract void ConfigureEmit(
        EmitBuilder emit,
        string topic,
        string groupId,
        TimeSpan workerStopTimeout);

    /// <summary>
    /// Verifies that a rebalance while a handler outlasts the worker stop timeout cancels that handler before the
    /// partition is handed over: no key is ever handled by two handlers at once, and each message is handled
    /// after the one before it.
    /// </summary>
    [Fact]
    public async Task GivenHandlerOutlastingTheStopTimeout_WhenTheGroupRebalances_ThenNoKeyIsHandledTwiceAtOnceAndOrderHolds()
    {
        // Arrange: handlings wait until the test opens the tracker, so the rebalance happens while one runs
        var topic = $"test-rebalance-order-{Guid.NewGuid():N}";
        var groupId = $"test-rebalance-order-group-{Guid.NewGuid():N}";
        var tracker = new HandlingTracker(closed: true);
        // Far longer than any handler takes to stop once cancelled, however busy the machine: the handler waits on
        // the tracker, so the revoke always waits this long before cancelling it
        var workerStopTimeout = TimeSpan.FromSeconds(30);
        var stopsOutlasted = TimeSpan.FromMinutes(2);

        IHost BuildHost(string name) => Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(tracker);
                services.AddSingleton(new ConsumerHost(name));
                services.AddEmit(emit => ConfigureEmit(emit, topic, groupId, workerStopTimeout));
            })
            .Build();

        using var first = BuildHost("first");
        using var second = BuildHost("second");
        await first.StartAsync();

        try
        {
            using (var scope = first.Services.CreateScope())
            {
                var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
                foreach (var value in new[] { "k1-1", "k1-2", "k1-3" })
                {
                    await producer.ProduceAsync(new EventMessage<string, string>("k1", value));
                }
            }

            await TestHelpers.WaitUntilAsync(() => tracker.WaitingOn("first") > 0, "The first message was not handled.");

            // Act: a second host joins the group, revoking the partition while the first message is handled
            await second.StartAsync();
            await TestHelpers.WaitUntilAsync(
                () => tracker.Cancelled > 0, "The handling running during the rebalance was not cancelled.", stopsOutlasted);
            tracker.Open();

            // Assert
            await TestHelpers.WaitUntilAsync(
                () => tracker.Completed.Contains("k1-3"), "Not every message was handled after the rebalance.", stopsOutlasted);
            Assert.Equal(1, tracker.MostAtOnceForAKey);
            Assert.Equal(["k1-1", "k1-2", "k1-3"], tracker.Completed.Distinct());
        }
        finally
        {
            await second.StopAsync();
            await first.StopAsync();
        }
    }

    /// <summary>
    /// Records the handlings of every host's <see cref="TrackingConsumer"/>: how many run at once for a key and on a
    /// host, how many wait, how many were cancelled, and the order they complete in. While closed, every handling
    /// waits until the tracker is opened or the handling cancelled. A message is <c>{key}-{sequence}</c>.
    /// </summary>
    public sealed class HandlingTracker
    {
        private readonly Lock sync = new();
        private readonly TaskCompletionSource opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> completed = [];
        private readonly Dictionary<string, int> runningForKey = [];
        private readonly Dictionary<string, int> runningOnHost = [];
        private readonly Dictionary<string, int> waitingOnHost = [];
        private int mostAtOnceForAKey;
        private int mostAtOnceOnAHost;
        private int cancelled;

        /// <summary>
        /// Creates a tracker.
        /// </summary>
        /// <param name="closed">Whether handlings wait until <see cref="Open"/>.</param>
        public HandlingTracker(bool closed = false)
        {
            if (!closed)
            {
                opened.SetResult();
            }
        }

        /// <summary>The messages whose handling completed, in order.</summary>
        public IReadOnlyList<string> Completed
        {
            get { lock (sync) { return [.. completed]; } }
        }

        /// <summary>The most handlings that ran at once for the same key.</summary>
        public int MostAtOnceForAKey
        {
            get { lock (sync) { return mostAtOnceForAKey; } }
        }

        /// <summary>The most handlings that ran at once on the same host.</summary>
        public int MostAtOnceOnAHost
        {
            get { lock (sync) { return mostAtOnceOnAHost; } }
        }

        /// <summary>The handlings cancelled while waiting.</summary>
        public int Cancelled
        {
            get { lock (sync) { return cancelled; } }
        }

        /// <summary>Returns how many handlings wait on a host.</summary>
        public int WaitingOn(string host)
        {
            lock (sync)
            {
                return waitingOnHost.GetValueOrDefault(host);
            }
        }

        /// <summary>Lets every handling, waiting or to come, complete.</summary>
        public void Open() => opened.TrySetResult();

        /// <summary>Handles a message, waiting while the tracker is closed.</summary>
        public async Task HandleAsync(string message, string host, CancellationToken cancellationToken)
        {
            var key = message[..message.LastIndexOf('-')];
            lock (sync)
            {
                mostAtOnceForAKey = Math.Max(mostAtOnceForAKey, Increment(runningForKey, key));
                mostAtOnceOnAHost = Math.Max(mostAtOnceOnAHost, Increment(runningOnHost, host));
                Increment(waitingOnHost, host);
            }

            try
            {
                try
                {
                    await opened.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    lock (sync) { cancelled++; }
                    throw;
                }
                finally
                {
                    lock (sync) { waitingOnHost[host]--; }
                }

                lock (sync) { completed.Add(message); }
            }
            finally
            {
                lock (sync)
                {
                    runningForKey[key]--;
                    runningOnHost[host]--;
                }
            }
        }

        private static int Increment(Dictionary<string, int> counts, string name) =>
            counts[name] = counts.GetValueOrDefault(name) + 1;
    }

    /// <summary>
    /// The name of the host a <see cref="TrackingConsumer"/> runs on, when a test runs several.
    /// </summary>
    /// <param name="Name">The host name.</param>
    public sealed record ConsumerHost(string Name);

    /// <summary>
    /// Consumer handling each message through the shared <see cref="HandlingTracker"/>.
    /// </summary>
    public sealed class TrackingConsumer(HandlingTracker tracker, ConsumerHost? host = null) : IConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
            => tracker.HandleAsync(context.Message, host?.Name ?? string.Empty, cancellationToken);
    }
}

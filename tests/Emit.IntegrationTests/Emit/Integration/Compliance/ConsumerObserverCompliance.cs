namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.DependencyInjection;
using Emit.Kafka.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for <see cref="IKafkaConsumerObserver"/> consumer lifecycle events.
/// Derived classes configure a Kafka topic and consumer group. The test verifies that
/// <c>OnConsumerStartedAsync</c> and <c>OnConsumerStoppedAsync</c> fire at the expected
/// points in the consumer group worker lifecycle.
/// </summary>
[Trait("Category", "Integration")]
public abstract class ConsumerObserverCompliance
{
    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a consumer
    /// group. The <see cref="TrackingKafkaConsumerObserver"/> must be registered as an
    /// <see cref="IKafkaConsumerObserver"/> so that lifecycle events are forwarded to it.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    protected abstract void ConfigureWithConsumerObserver(EmitBuilder emit, string topic, string groupId);

    /// <summary>
    /// Verifies that when the host starts, <c>OnConsumerStartedAsync</c> fires with the
    /// correct group ID and topic.
    /// </summary>
    [Fact]
    public async Task GivenKafkaConsumerObserver_WhenHostStarted_ThenOnConsumerStartedFires()
    {
        // Arrange
        var topic = $"test-cobs-started-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var observer = new TrackingKafkaConsumerObserver();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<TrackingKafkaConsumerObserver>(observer);
                services.AddEmit(emit =>
                {
                    emit.Services.AddSingleton<IKafkaConsumerObserver>(
                        sp => sp.GetRequiredService<TrackingKafkaConsumerObserver>());
                    ConfigureWithConsumerObserver(emit, topic, groupId);
                });
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Assert — wait for the started callback to fire.
            await observer.WaitForStartedAsync(TimeSpan.FromSeconds(30));

            Assert.True(observer.StartedCount > 0,
                $"Expected OnConsumerStartedAsync to be called at least once but count was {observer.StartedCount}.");

            var startedEvent = observer.LastStartedEvent;
            Assert.NotNull(startedEvent);
            Assert.Equal(groupId, startedEvent.GroupId);
            Assert.Equal(topic, startedEvent.Topic);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that when the host stops, <c>OnConsumerStoppedAsync</c> fires with the
    /// correct group ID and topic.
    /// </summary>
    [Fact]
    public async Task GivenKafkaConsumerObserver_WhenHostStopped_ThenOnConsumerStoppedFires()
    {
        // Arrange
        var topic = $"test-cobs-stopped-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var observer = new TrackingKafkaConsumerObserver();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<TrackingKafkaConsumerObserver>(observer);
                services.AddEmit(emit =>
                {
                    emit.Services.AddSingleton<IKafkaConsumerObserver>(
                        sp => sp.GetRequiredService<TrackingKafkaConsumerObserver>());
                    ConfigureWithConsumerObserver(emit, topic, groupId);
                });
            })
            .Build();

        await host.StartAsync();

        // Wait for the consumer to start before stopping.
        await observer.WaitForStartedAsync(TimeSpan.FromSeconds(30));

        // Act
        await host.StopAsync();
        host.Dispose();

        // Assert — stopped callback must have fired by now (StopAsync completes after worker stops).
        Assert.True(observer.StoppedCount > 0,
            $"Expected OnConsumerStoppedAsync to be called at least once but count was {observer.StoppedCount}.");

        var stoppedEvent = observer.LastStoppedEvent;
        Assert.NotNull(stoppedEvent);
        Assert.Equal(groupId, stoppedEvent.GroupId);
        Assert.Equal(topic, stoppedEvent.Topic);
    }

    /// <summary>
    /// <see cref="IKafkaConsumerObserver"/> implementation that tracks lifecycle event counts.
    /// </summary>
    public sealed class TrackingKafkaConsumerObserver : IKafkaConsumerObserver
    {
        private int startedCount;
        private int stoppedCount;
        private readonly SemaphoreSlim startedSignal = new(0);
        private volatile ConsumerStartedEvent? lastStartedEvent;
        private volatile ConsumerStoppedEvent? lastStoppedEvent;

        /// <summary>Gets the number of times <see cref="IKafkaConsumerObserver.OnConsumerStartedAsync"/> was called.</summary>
        public int StartedCount => Volatile.Read(ref startedCount);

        /// <summary>Gets the number of times <see cref="IKafkaConsumerObserver.OnConsumerStoppedAsync"/> was called.</summary>
        public int StoppedCount => Volatile.Read(ref stoppedCount);

        /// <summary>Gets the most recent <see cref="ConsumerStartedEvent"/> received.</summary>
        public ConsumerStartedEvent? LastStartedEvent => lastStartedEvent;

        /// <summary>Gets the most recent <see cref="ConsumerStoppedEvent"/> received.</summary>
        public ConsumerStoppedEvent? LastStoppedEvent => lastStoppedEvent;

        /// <inheritdoc />
        public Task OnConsumerStartedAsync(ConsumerStartedEvent e)
        {
            lastStartedEvent = e;
            Interlocked.Increment(ref startedCount);
            startedSignal.Release();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task OnConsumerStoppedAsync(ConsumerStoppedEvent e)
        {
            lastStoppedEvent = e;
            Interlocked.Increment(ref stoppedCount);
            return Task.CompletedTask;
        }

        /// <summary>Waits until at least one started callback fires.</summary>
        public async Task WaitForStartedAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            await startedSignal.WaitAsync(cts.Token);
        }
    }
}

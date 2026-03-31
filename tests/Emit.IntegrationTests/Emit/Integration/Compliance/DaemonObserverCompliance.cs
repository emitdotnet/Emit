namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions.Observability;
using Emit.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Compliance tests for <see cref="IDaemonObserver"/>. Derived classes configure a persistence
/// provider so that the daemon coordinator and leader election are activated. The test verifies
/// that <c>OnDaemonAssignedAsync</c> and <c>OnDaemonStartedAsync</c> both fire when the host
/// starts and a daemon is assigned to this node.
/// Kafka configuration is handled by the base class.
/// </summary>
[Trait("Category", "Integration")]
public abstract class DaemonObserverCompliance : IAsyncLifetime
{
    /// <summary>
    /// Gets the Kafka bootstrap servers address for producing and consuming messages.
    /// </summary>
    protected abstract string BootstrapServers { get; }

    /// <summary>
    /// Configures the persistence provider (MongoDB or EF Core) with outbox enabled.
    /// Kafka configuration is handled by the base class.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    protected abstract void ConfigurePersistence(EmitBuilder emit);

    /// <inheritdoc />
    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Verifies that when the host starts and the daemon coordinator assigns and starts the
    /// outbox daemon on this node, both <c>OnDaemonAssignedAsync</c> and
    /// <c>OnDaemonStartedAsync</c> fire.
    /// </summary>
    [Fact]
    public async Task GivenDaemonObserver_WhenDaemonAssignedAndStarted_ThenAssignedAndStartedCallbacksFire()
    {
        // Arrange
        var topic = $"test-daemon-obs-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var observer = new TrackingDaemonObserver();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<TrackingDaemonObserver>(observer);
                services.AddEmit(emit =>
                {
                    services.AddSingleton<IDaemonObserver>(
                        sp => sp.GetRequiredService<TrackingDaemonObserver>());
                    ConfigurePersistence(emit);
                    ConfigureKafka(emit, topic, groupId);
                });
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Assert — wait for the daemon to be assigned and started.
            // The heartbeat worker runs on a short interval in tests; allow generous timeout.
            await observer.WaitForStartedAsync(TimeSpan.FromSeconds(30));

            Assert.True(observer.AssignedCount > 0,
                $"Expected OnDaemonAssignedAsync to be called at least once but count was {observer.AssignedCount}.");
            Assert.True(observer.StartedCount > 0,
                $"Expected OnDaemonStartedAsync to be called at least once but count was {observer.StartedCount}.");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private void ConfigureKafka(EmitBuilder emit, string topic, string groupId)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config => config.BootstrapServers = BootstrapServers);
            kafka.AutoProvision();

            kafka.Topic<string, string>(topic, t =>
            {
                t.SetUtf8KeySerializer();
                t.SetUtf8ValueSerializer();
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.Producer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<SinkConsumer<string>>();
                });
            });
        });
    }

    /// <summary>
    /// <see cref="IDaemonObserver"/> implementation that tracks invocation counts for
    /// each observer callback.
    /// </summary>
    public sealed class TrackingDaemonObserver : IDaemonObserver
    {
        private int assignedCount;
        private int startedCount;
        private int stoppedCount;
        private int revokedCount;
        private readonly SemaphoreSlim startedSignal = new(0);

        /// <summary>Gets the number of times <see cref="IDaemonObserver.OnDaemonAssignedAsync"/> was called.</summary>
        public int AssignedCount => Volatile.Read(ref assignedCount);

        /// <summary>Gets the number of times <see cref="IDaemonObserver.OnDaemonStartedAsync"/> was called.</summary>
        public int StartedCount => Volatile.Read(ref startedCount);

        /// <summary>Gets the number of times <see cref="IDaemonObserver.OnDaemonStoppedAsync"/> was called.</summary>
        public int StoppedCount => Volatile.Read(ref stoppedCount);

        /// <summary>Gets the number of times <see cref="IDaemonObserver.OnDaemonRevokedAsync"/> was called.</summary>
        public int RevokedCount => Volatile.Read(ref revokedCount);

        /// <inheritdoc />
        public Task OnDaemonAssignedAsync(string daemonId, Guid nodeId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref assignedCount);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task OnDaemonStartedAsync(string daemonId, Guid nodeId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref startedCount);
            startedSignal.Release();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task OnDaemonStoppedAsync(string daemonId, Guid nodeId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref stoppedCount);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task OnDaemonRevokedAsync(string daemonId, Guid nodeId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref revokedCount);
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

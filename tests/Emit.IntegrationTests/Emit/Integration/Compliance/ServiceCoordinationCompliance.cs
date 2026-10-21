namespace Emit.IntegrationTests.Integration.Compliance;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Emit.Abstractions;
using Emit.Abstractions.Daemon;
using Emit.Abstractions.LeaderElection;
using Emit.Abstractions.Metrics;
using Emit.Abstractions.Observability;
using Emit.Configuration;
using Emit.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;
using static Emit.IntegrationTests.Integration.TestHelpers;

/// <summary>
/// Compliance tests for daemon coordination on real hosts: services sharing one database each elect their own
/// leader and run their own daemons on their own nodes, and a node that shuts down hands its daemon and its
/// leadership over to another node of its service. Derived classes configure a persistence provider; every
/// host built by a test points at the same database.
/// </summary>
[Trait("Category", "Integration")]
public abstract class ServiceCoordinationCompliance : IAsyncLifetime
{
    /// <summary>
    /// Configures the persistence provider. Every call must target the same database.
    /// </summary>
    protected abstract void ConfigurePersistence(EmitBuilder emit);

    /// <inheritdoc />
    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GivenApiLeadsFirst_WhenWorkerWithDaemonsStarts_ThenEachServiceLeadsAndRunsItsOwnDaemonsAndItsObserversHearOfThem()
    {
        // Arrange: the API is already leading when the worker starts, and both services register a daemon with
        // the same id
        var apiShared = new SignalingDaemonAgent("shared-daemon");
        var workerShared = new SignalingDaemonAgent("shared-daemon");
        var searchIndex = new SignalingDaemonAgent("search-index");

        using var api = BuildHost($"api-{Guid.NewGuid():N}", apiShared);
        using var worker = BuildHost($"worker-{Guid.NewGuid():N}", observed: true, workerShared, searchIndex);

        try
        {
            await api.StartAsync();
            await WaitUntilAsync(() => IsLeader(api), "The API did not become leader within timeout.");

            // Act
            await worker.StartAsync();

            // Assert
            await apiShared.WaitForStartAsync(TimeSpan.FromSeconds(30));
            await workerShared.WaitForStartAsync(TimeSpan.FromSeconds(30));
            await searchIndex.WaitForStartAsync(TimeSpan.FromSeconds(30));
            await WaitUntilAsync(
                () => IsLeader(api) && IsLeader(worker),
                "The API and the worker did not both hold the leadership of their service within timeout.");
            var daemonObserver = worker.Services.GetServices<IDaemonObserver>().OfType<RecordingObserver>().Single();
            var leaderElectionObserver = worker.Services.GetServices<ILeaderElectionObserver>().OfType<RecordingObserver>().Single();
            await WaitUntilAsync(
                () => new[] { daemonObserver.Assigned, daemonObserver.Started }.All(heard => heard.Contains("shared-daemon") && heard.Contains("search-index"))
                    && leaderElectionObserver.ElectedNodes.Contains(worker.Services.GetRequiredService<INodeIdentity>().NodeId),
                "The worker's observers, registered on the Emit builder, did not hear of its election and of its daemons being assigned and started.");
        }
        finally
        {
            await worker.StopAsync();
            await api.StopAsync();
        }
    }

    [Fact]
    public async Task GivenNodeLeadingAndRunningTheDaemon_WhenItShutsDown_ThenItDeregistersItsObserversHearTheDaemonStopAndTheOtherNodeOfItsServiceTakesOver()
    {
        // Arrange: the first node starts alone, so it leads and runs the daemon before the second one joins. Its run
        // takes a while to stop, within the drain timeout, and must have ended before the other node starts it.
        var service = NewServiceName();
        var runs = new ConcurrentQueue<string>();
        var onFirst = new SignalingDaemonAgent("singleton", runs, "first", stopsAfter: TimeSpan.FromMilliseconds(300));
        var onSecond = new SignalingDaemonAgent("singleton", runs, "second");
        using var first = BuildHost(service, observed: true, onFirst);
        using var second = BuildHost(service, onSecond);

        try
        {
            await first.StartAsync();
            await onFirst.WaitForStartAsync(TimeSpan.FromSeconds(30));
            await WaitUntilAsync(() => IsLeader(first), "The first node did not lead its service within timeout.");
            await second.StartAsync();
            var persistence = second.Services.GetRequiredService<ILeaderElectionPersistence>();
            var secondNodeId = second.Services.GetRequiredService<INodeIdentity>().NodeId;
            await WaitUntilAsync(
                async () => (await persistence.GetNodesAsync(service)).Any(n => n.NodeId == secondNodeId),
                "The second node did not register within timeout.");

            // Act
            await first.StopAsync();

            // Assert: the node gave up its lease and its daemon before it deregistered, so neither waits to expire.
            // A heartbeat that does not claim the lease reports who holds it without taking it; it declares no
            // daemon and is removed at once, so the service's leader places nothing on it.
            // Its observers heard the daemon stop before the host went on to dispose them
            Assert.Contains("singleton", Observer(first).Stopped);
            var stoppedNodeId = first.Services.GetRequiredService<INodeIdentity>().NodeId;
            var registered = await persistence.GetNodesAsync(service);
            Assert.DoesNotContain(registered, n => n.NodeId == stoppedNodeId);
            var assignments = await second.Services.GetRequiredService<IDaemonAssignmentPersistence>().GetAllAssignmentsAsync(service);
            Assert.DoesNotContain(assignments, a => a.AssignedNodeId == stoppedNodeId);
            var timings = second.Services.GetRequiredService<IOptions<LeaderElectionOptions>>().Value;
            var probe = Guid.NewGuid();
            var lease = await persistence.HeartbeatAsync(new HeartbeatRequest(
                service, probe, "probe", [], timings.LeaseDuration, timings.NodeRegistrationTtl, Guid.NewGuid(), ClaimsLease: false));
            await persistence.DeregisterNodeAsync(probe);
            Assert.NotEqual(stoppedNodeId, lease.LeaderNodeId);
            await onSecond.WaitForStartAsync(TimeSpan.FromSeconds(30));
            await WaitUntilAsync(() => IsLeader(second), "The remaining node did not lead its service within timeout.");
            // A run fenced by a slow heartbeat restarts on the same node, so only the order of the two nodes' runs counts
            var log = runs.ToList();
            Assert.Equal(log.Count(r => r == "first started"), log.Count(r => r == "first ended"));
            Assert.True(log.LastIndexOf("first ended") < log.IndexOf("second started"));
        }
        finally
        {
            await first.StopAsync();
            await second.StopAsync();
        }
    }

    [Fact]
    public async Task GivenNodeRunningEveryDaemon_WhenAnotherNodeJoinsAndThenTheFirstLosesTheDatabase_ThenTheDaemonsSpreadThenMoveAndTwoNodesNeverActForOneDaemonAtOnce()
    {
        // Arrange: the first node starts alone and takes both daemons. Each daemon records a side effect about
        // every 100 ms, only while its run holds the assignment. Runs are not compared by when they start and end:
        // a run that is paused can legitimately still be going when another node already runs the daemon, which is
        // what holding the assignment guards against.
        var service = NewServiceName();
        var effects = new ConcurrentQueue<SideEffect>();
        var outage = new PersistenceOutage();
        EffectDaemonAgent[] onFirst = [new("d1", "first", effects), new("d2", "first", effects)];
        EffectDaemonAgent[] onSecond = [new("d1", "second", effects), new("d2", "second", effects)];
        using var first = BuildHost(service, observed: true, outage, onFirst);
        using var second = BuildHost(service, observed: true, outage: null, onSecond);
        using var runs = new DaemonRunsGauge();

        try
        {
            await first.StartAsync();
            await Task.WhenAll(onFirst.Select(d => d.WaitForStartAsync(TimeSpan.FromSeconds(60))));

            // Act: a second node joins
            await second.StartAsync();

            // Assert: one daemon moved to it, revoked from the first node rather than running on both
            await WaitUntilAsync(
                () => onSecond.Count(d => d.HasStarted) == 1,
                "No daemon moved to the node that joined within timeout.",
                TimeSpan.FromSeconds(60));
            var moved = onSecond.Single(d => d.HasStarted).DaemonId;
            var stayed = onSecond.Single(d => !d.HasStarted).DaemonId;
            await WaitUntilAsync(
                () => runs.Running(first, moved) == 0 && runs.Running(second, moved) == 1
                    && runs.Running(first, stayed) == 1 && runs.Running(second, stayed) == 0,
                "The runs gauges of the two nodes did not show each daemon running once, where it was placed.",
                TimeSpan.FromSeconds(60));
            await WaitUntilAsync(
                () => new[] { first, second }.Any(host => Observer(host).Revoked.Contains(moved)),
                "The leader's observers did not hear of the daemon being revoked to balance the service.");

            // Act: the first node loses the database, without shutting down
            outage.Begin();

            // Assert: its daemon moves to the second node once its registration expires, and it stops reporting it
            await onSecond.Single(d => d.DaemonId == stayed).WaitForStartAsync(TimeSpan.FromSeconds(60));
            await WaitUntilAsync(
                () => runs.Running(first, stayed) == 0 && runs.Running(second, stayed) == 1,
                "The node cut off from the database still reported the daemon it no longer holds as running.",
                TimeSpan.FromSeconds(60));
            await WaitUntilAsync(
                () => effects.Any(e => e.DaemonId == stayed && e.Node == "second"),
                "The daemon that failed over had no side effect on its new node within timeout.");

            foreach (var daemonId in new[] { moved, stayed })
            {
                var onFirstNode = effects.Where(e => e.DaemonId == daemonId && e.Node == "first").ToList();
                var onSecondNode = effects.Where(e => e.DaemonId == daemonId && e.Node == "second").ToList();
                Assert.NotEmpty(onFirstNode);
                Assert.NotEmpty(onSecondNode);
                Assert.True(
                    onFirstNode.Max(e => e.CheckedAt) < onSecondNode.Min(e => e.CheckedAt),
                    $"Daemon {daemonId} acted on both nodes at once.");
                Assert.True(
                    onSecondNode.Min(e => e.Generation) > onFirstNode.Max(e => e.Generation),
                    $"Daemon {daemonId} did not run under a later generation on the node it moved to.");
                Assert.Single(onFirstNode.Concat(onSecondNode).Select(e => e.FencingKey).Distinct());
            }
        }
        finally
        {
            await second.StopAsync();
            await first.StopAsync();
        }
    }

    // --- Helpers ---

    private IHost BuildHost(string serviceName, params IDaemonAgent[] daemons) =>
        BuildHost(serviceName, observed: false, outage: null, daemons);

    private IHost BuildHost(string serviceName, bool observed, params IDaemonAgent[] daemons) =>
        BuildHost(serviceName, observed, outage: null, daemons);

    private IHost BuildHost(string serviceName, bool observed, PersistenceOutage? outage, params IDaemonAgent[] daemons) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddEmit(emit =>
                {
                    ConfigureFastCoordination(emit, serviceName);
                    ConfigurePersistence(emit);
                    outage?.Install(emit.Services);
                    foreach (var daemon in daemons)
                    {
                        emit.AddDaemon(_ => daemon);
                    }

                    if (observed)
                    {
                        emit.AddDaemonObserver<RecordingObserver>();
                        emit.AddLeaderElectionObserver<RecordingObserver>();
                    }
                });
            })
            .Build();

    private static bool IsLeader(IHost host) => host.Services.GetRequiredService<ILeaderElectionService>().IsLeader;

    private static RecordingObserver Observer(IHost host) =>
        host.Services.GetServices<IDaemonObserver>().OfType<RecordingObserver>().Single();

    /// <summary>
    /// Records the daemons assigned, started, revoked and stopped, and the nodes elected, as this node's observers
    /// hear of them.
    /// </summary>
    private sealed class RecordingObserver : IDaemonObserver, ILeaderElectionObserver
    {
        public ConcurrentBag<string> Assigned { get; } = [];

        public ConcurrentBag<string> Started { get; } = [];

        public ConcurrentBag<string> Revoked { get; } = [];

        public ConcurrentBag<string> Stopped { get; } = [];

        public ConcurrentBag<Guid> ElectedNodes { get; } = [];

        public Task OnDaemonRevokedAsync(string daemonId, Guid nodeId, CancellationToken cancellationToken)
        {
            Revoked.Add(daemonId);
            return Task.CompletedTask;
        }

        public Task OnDaemonStoppedAsync(string daemonId, Guid nodeId, CancellationToken cancellationToken)
        {
            Stopped.Add(daemonId);
            return Task.CompletedTask;
        }

        public Task OnLeaderElectedAsync(Guid nodeId, CancellationToken cancellationToken)
        {
            ElectedNodes.Add(nodeId);
            return Task.CompletedTask;
        }

        public Task OnDaemonAssignedAsync(string daemonId, Guid nodeId, CancellationToken cancellationToken)
        {
            Assigned.Add(daemonId);
            return Task.CompletedTask;
        }

        public Task OnDaemonStartedAsync(string daemonId, Guid nodeId, CancellationToken cancellationToken)
        {
            Started.Add(daemonId);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Daemon that signals when it is started, and records when its runs start and end in a log it may share
    /// with daemons of other nodes. A run ends <c>stopsAfter</c> after it is cancelled.
    /// </summary>
    private sealed class SignalingDaemonAgent(
        string daemonId,
        ConcurrentQueue<string>? runs = null,
        string? label = null,
        TimeSpan stopsAfter = default) : IDaemonAgent
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string DaemonId { get; } = daemonId;

        public async Task RunAsync(DaemonRunContext context)
        {
            var assignmentToken = context.AssignmentToken;
            runs?.Enqueue($"{label} started");
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, assignmentToken);
            }
            catch (OperationCanceledException)
            {
                // Stopped by the coordinator
            }

            await Task.Delay(stopsAfter, CancellationToken.None);
            runs?.Enqueue($"{label} ended");
        }

        public Task WaitForStartAsync(TimeSpan timeout) => started.Task.WaitAsync(timeout);
    }

    /// <summary>
    /// A side effect of a daemon run, with the moment its run checked it still held the assignment, taken just
    /// before the check so that a run holding it at the check also held it then.
    /// </summary>
    private sealed record SideEffect(string DaemonId, string Node, long Generation, Guid FencingKey, long CheckedAt);

    /// <summary>
    /// Daemon that records a side effect about every 100 ms while its run holds the assignment, in a log it shares
    /// with the daemons of other nodes, and signals its first start.
    /// </summary>
    private sealed class EffectDaemonAgent(string daemonId, string node, ConcurrentQueue<SideEffect> effects) : IDaemonAgent
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string DaemonId { get; } = daemonId;

        public bool HasStarted => started.Task.IsCompleted;

        public async Task RunAsync(DaemonRunContext context)
        {
            started.TrySetResult();
            while (!context.AssignmentToken.IsCancellationRequested)
            {
                var checkedAt = Stopwatch.GetTimestamp();
                if (context.HoldsAssignment)
                {
                    effects.Enqueue(new SideEffect(DaemonId, node, context.Generation, context.FencingKey, checkedAt));
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), context.AssignmentToken);
                }
                catch (OperationCanceledException)
                {
                    // Stopped by the coordinator
                }
            }
        }

        public Task WaitForStartAsync(TimeSpan timeout) => started.Task.WaitAsync(timeout);
    }

    /// <summary>
    /// Reads the <c>emit.daemon.runs</c> gauge of each node. Measurements are kept apart by the node they came from,
    /// so the gauges of hosts in other tests running at the same time are ignored.
    /// </summary>
    private sealed class DaemonRunsGauge : IDisposable
    {
        private readonly MeterListener listener = new();
        private readonly ConcurrentDictionary<(string Node, string DaemonId), int> running = new();

        public DaemonRunsGauge()
        {
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == MeterNames.EmitDaemon && instrument.Name == "emit.daemon.runs")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<int>((_, value, tags, _) =>
            {
                string? node = null, daemonId = null, state = null;
                foreach (var tag in tags)
                {
                    switch (tag.Key)
                    {
                        case "emit.node.id":
                            node = tag.Value as string;
                            break;
                        case "daemon":
                            daemonId = tag.Value as string;
                            break;
                        case "state":
                            state = tag.Value as string;
                            break;
                    }
                }

                if (node is not null && daemonId is not null && state == "running")
                {
                    running[(node, daemonId)] = value;
                }
            });
            listener.Start();
        }

        /// <summary>
        /// Returns how many runs of the daemon the host reports running now.
        /// </summary>
        public int Running(IHost host, string daemonId)
        {
            var node = host.Services.GetRequiredService<INodeIdentity>().NodeId.ToString();
            running.TryRemove((node, daemonId), out _);
            listener.RecordObservableInstruments();
            return running.TryGetValue((node, daemonId), out var value) ? value : -1;
        }

        public void Dispose() => listener.Dispose();
    }
}

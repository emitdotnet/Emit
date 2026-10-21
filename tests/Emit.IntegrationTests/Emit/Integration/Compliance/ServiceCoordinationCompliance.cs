namespace Emit.IntegrationTests.Integration.Compliance;

using System.Collections.Concurrent;
using Emit.Abstractions;
using Emit.Abstractions.Daemon;
using Emit.Abstractions.LeaderElection;
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
    public async Task GivenNodeLeadingAndRunningTheDaemon_WhenItShutsDown_ThenItDeregistersAndTheOtherNodeOfItsServiceTakesOver()
    {
        // Arrange: the first node starts alone, so it leads and runs the daemon before the second one joins. Its run
        // takes a while to stop, within the drain timeout, and must have ended before the other node starts it.
        var service = NewServiceName();
        var runs = new ConcurrentQueue<string>();
        var onFirst = new SignalingDaemonAgent("singleton", runs, "first", stopsAfter: TimeSpan.FromMilliseconds(300));
        var onSecond = new SignalingDaemonAgent("singleton", runs, "second");
        using var first = BuildHost(service, onFirst);
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

    // --- Helpers ---

    private IHost BuildHost(string serviceName, params IDaemonAgent[] daemons) =>
        BuildHost(serviceName, observed: false, daemons);

    private IHost BuildHost(string serviceName, bool observed, params IDaemonAgent[] daemons) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                foreach (var daemon in daemons)
                {
                    services.AddSingleton(daemon);
                }

                services.AddEmit(emit =>
                {
                    ConfigureFastCoordination(emit, serviceName);
                    ConfigurePersistence(emit);
                    if (observed)
                    {
                        emit.AddDaemonObserver<RecordingObserver>();
                        emit.AddLeaderElectionObserver<RecordingObserver>();
                    }
                });
            })
            .Build();

    private static bool IsLeader(IHost host) => host.Services.GetRequiredService<ILeaderElectionService>().IsLeader;

    /// <summary>
    /// Records the daemons assigned and started, and the nodes elected, as this node's observers hear of them.
    /// </summary>
    private sealed class RecordingObserver : IDaemonObserver, ILeaderElectionObserver
    {
        public ConcurrentBag<string> Assigned { get; } = [];

        public ConcurrentBag<string> Started { get; } = [];

        public ConcurrentBag<Guid> ElectedNodes { get; } = [];

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
}

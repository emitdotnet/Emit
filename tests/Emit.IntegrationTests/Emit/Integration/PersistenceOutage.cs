namespace Emit.IntegrationTests.Integration;

using Emit.Abstractions.Daemon;
using Emit.Abstractions.LeaderElection;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Makes one host stop heartbeating without shutting down: once <see cref="Begin"/> is called, every leader election
/// and daemon assignment call of that host fails at once. To the other nodes it looks like a node that crashed, froze
/// or lost its connection to the database; the host itself keeps running, as a frozen or disconnected node does.
/// Hosts sharing a test process cannot be crashed, and stopping one always hands its daemons over gracefully, so this
/// is how a test reaches the failover that waits for a node's registration to expire.
/// </summary>
internal sealed class PersistenceOutage
{
    private volatile bool active;

    /// <summary>
    /// Starts the outage: from now on every call fails.
    /// </summary>
    public void Begin() => active = true;

    /// <summary>
    /// Wraps the coordination persistence registered by the host's persistence provider. Call it after the
    /// provider is registered.
    /// </summary>
    public void Install(IServiceCollection services)
    {
        Wrap<ILeaderElectionPersistence>(services, inner => new FailingLeaderElectionPersistence(inner, this));
        Wrap<IDaemonAssignmentPersistence>(services, inner => new FailingDaemonAssignmentPersistence(inner, this));
    }

    private Task<T> Call<T>(Func<Task<T>> call) =>
        active ? Task.FromException<T>(new InvalidOperationException("The database is unreachable.")) : call();

    private Task Call(Func<Task> call) =>
        active ? Task.FromException(new InvalidOperationException("The database is unreachable.")) : call();

    private static void Wrap<T>(IServiceCollection services, Func<T, T> wrap) where T : class
    {
        var registered = services.Last(d => !d.IsKeyedService && d.ServiceType == typeof(T));
        services.Remove(registered);
        services.AddSingleton(sp => wrap(registered switch
        {
            { ImplementationFactory: { } factory } => (T)factory(sp),
            { ImplementationInstance: { } instance } => (T)instance,
            _ => (T)ActivatorUtilities.CreateInstance(sp, registered.ImplementationType!),
        }));
    }

    private sealed class FailingLeaderElectionPersistence(ILeaderElectionPersistence inner, PersistenceOutage outage) : ILeaderElectionPersistence
    {
        public Task<HeartbeatResult> HeartbeatAsync(HeartbeatRequest request, CancellationToken cancellationToken = default) =>
            outage.Call(() => inner.HeartbeatAsync(request, cancellationToken));

        public Task ResignLeadershipAsync(string serviceName, Guid nodeId, IReadOnlyCollection<Guid> heartbeatIds, CancellationToken cancellationToken = default) =>
            outage.Call(() => inner.ResignLeadershipAsync(serviceName, nodeId, heartbeatIds, cancellationToken));

        public Task<IReadOnlyList<Guid>> RemoveNodesAsync(string serviceName, IReadOnlyCollection<NodeRegistration> nodes, CancellationToken cancellationToken = default) =>
            outage.Call(() => inner.RemoveNodesAsync(serviceName, nodes, cancellationToken));

        public Task DeregisterNodeAsync(Guid nodeId, CancellationToken cancellationToken = default) =>
            outage.Call(() => inner.DeregisterNodeAsync(nodeId, cancellationToken));

        public Task<IReadOnlyList<NodeRegistration>> GetNodesAsync(string serviceName, CancellationToken cancellationToken = default) =>
            outage.Call(() => inner.GetNodesAsync(serviceName, cancellationToken));
    }

    private sealed class FailingDaemonAssignmentPersistence(IDaemonAssignmentPersistence inner, PersistenceOutage outage) : IDaemonAssignmentPersistence
    {
        public Task<DaemonAssignment?> AssignAsync(string serviceName, string daemonId, Guid nodeId, DaemonAssignment? expected, CancellationToken cancellationToken = default) =>
            outage.Call(() => inner.AssignAsync(serviceName, daemonId, nodeId, expected, cancellationToken));

        public Task<DaemonAssignment?> RevokeAsync(string serviceName, string daemonId, long expectedGeneration, CancellationToken cancellationToken = default) =>
            outage.Call(() => inner.RevokeAsync(serviceName, daemonId, expectedGeneration, cancellationToken));

        public Task<IReadOnlyList<DaemonAssignment>> GetAllAssignmentsAsync(string serviceName, CancellationToken cancellationToken = default) =>
            outage.Call(() => inner.GetAllAssignmentsAsync(serviceName, cancellationToken));

        public Task<IReadOnlyList<DaemonAssignment>> GetNodeAssignmentsAsync(string serviceName, Guid nodeId, CancellationToken cancellationToken = default) =>
            outage.Call(() => inner.GetNodeAssignmentsAsync(serviceName, nodeId, cancellationToken));

        public Task<bool> AcknowledgeAsync(string serviceName, string daemonId, long generation, CancellationToken cancellationToken = default) =>
            outage.Call(() => inner.AcknowledgeAsync(serviceName, daemonId, generation, cancellationToken));

        public Task<bool> ConfirmDrainAsync(string serviceName, string daemonId, long generation, CancellationToken cancellationToken = default) =>
            outage.Call(() => inner.ConfirmDrainAsync(serviceName, daemonId, generation, cancellationToken));

        public Task<bool> RemoveAsync(string serviceName, string daemonId, long generation, CancellationToken cancellationToken = default) =>
            outage.Call(() => inner.RemoveAsync(serviceName, daemonId, generation, cancellationToken));
    }
}

namespace Emit.UnitTests.Coordination;

using Emit.Abstractions.Daemon;
using Emit.Abstractions.LeaderElection;

/// <summary>
/// In-memory implementation of <see cref="ILeaderElectionPersistence"/> and
/// <see cref="IDaemonAssignmentPersistence"/> with the same semantics as the database providers.
/// Every operation is atomic, and time comes from the supplied <see cref="TimeProvider"/>, so
/// multi-node scenarios run deterministically. The same compliance suites that run against
/// MongoDB and PostgreSQL also run against this store.
/// </summary>
internal sealed class InMemoryCoordinationStore(TimeProvider timeProvider) : ILeaderElectionPersistence, IDaemonAssignmentPersistence
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, NodeRow> nodes = [];
    private readonly Dictionary<string, LeaseRow> leases = new(StringComparer.Ordinal);
    private readonly Dictionary<AssignmentKey, AssignmentRow> assignments = [];

    /// <summary>
    /// Creates a connection whose operations can be made to fail, simulating a node that lost
    /// connectivity to the database.
    /// </summary>
    public Connection Connect() => new(this);

    /// <summary>
    /// Returns every assignment with the service it belongs to.
    /// </summary>
    public IReadOnlyList<(string ServiceName, DaemonAssignment Assignment)> GetAssignmentsSnapshot()
    {
        lock (gate)
        {
            return assignments
                .Where(a => !a.Value.Released)
                .Select(a => (a.Key.ServiceName, ToAssignment(a.Key.DaemonId, a.Value)))
                .ToList();
        }
    }

    /// <inheritdoc />
    public Task<HeartbeatResult> HeartbeatAsync(HeartbeatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (gate)
        {
            nodes[request.NodeId] = new NodeRow(request.ServiceName, [.. request.DaemonIds], request.HeartbeatId, request.NodeTtl);

            leases.TryGetValue(request.ServiceName, out var lease);
            if (request.ClaimsLease
                && (lease is null
                    || lease.NodeId == request.NodeId
                    || (request.TakeOverLeaseHeartbeatId is { } unrenewed && lease.HeartbeatId == unrenewed)))
            {
                lease = new LeaseRow(request.NodeId, request.HeartbeatId, request.LeaseDuration);
                leases[request.ServiceName] = lease;
            }

            return Task.FromResult(lease is null
                ? new HeartbeatResult(false, Guid.Empty, Guid.Empty, TimeSpan.Zero)
                : new HeartbeatResult(request.ClaimsLease && lease.NodeId == request.NodeId, lease.NodeId, lease.HeartbeatId, lease.Duration));
        }
    }

    /// <inheritdoc />
    public Task ResignLeadershipAsync(
        string serviceName,
        Guid nodeId,
        IReadOnlyCollection<Guid> heartbeatIds,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (leases.TryGetValue(serviceName, out var lease) && lease.NodeId == nodeId && heartbeatIds.Contains(lease.HeartbeatId))
            {
                leases.Remove(serviceName);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> RemoveNodesAsync(
        string serviceName,
        IReadOnlyCollection<NodeRegistration> nodesToRemove,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<IReadOnlyList<Guid>>([]);
        }

        lock (gate)
        {
            List<Guid> removed = [];
            foreach (var node in nodesToRemove)
            {
                if (nodes.TryGetValue(node.NodeId, out var row) && row.ServiceName == serviceName && row.HeartbeatId == node.HeartbeatId)
                {
                    nodes.Remove(node.NodeId);
                    removed.Add(node.NodeId);
                }
            }

            return Task.FromResult<IReadOnlyList<Guid>>(removed);
        }
    }

    /// <inheritdoc />
    public Task DeregisterNodeAsync(Guid nodeId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            nodes.Remove(nodeId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<NodeRegistration>> GetNodesAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlyList<NodeRegistration> result = nodes
                .Where(n => n.Value.ServiceName == serviceName)
                .Select(n => new NodeRegistration(n.Key, n.Value.DaemonIds, n.Value.HeartbeatId, n.Value.Ttl))
                .ToList();
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc />
    public Task<DaemonAssignment?> AssignAsync(string serviceName, string daemonId, Guid nodeId, DaemonAssignment? expected, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var key = new AssignmentKey(serviceName, daemonId);
            assignments.TryGetValue(key, out var existing);
            var current = existing is { Released: false } ? existing : null;
            var expectationHolds = expected is null
                ? current is null
                : current is not null && current.Generation == expected.Generation && current.State == expected.State;
            if (!expectationHolds)
            {
                return Task.FromResult<DaemonAssignment?>(null);
            }

            var row = new AssignmentRow(
                nodeId,
                (existing?.Generation ?? 0) + 1,
                DaemonAssignmentState.Assigning,
                Now(),
                existing?.FencingKey ?? Guid.NewGuid());
            assignments[key] = row;
            return Task.FromResult<DaemonAssignment?>(ToAssignment(daemonId, row));
        }
    }

    /// <inheritdoc />
    public Task<DaemonAssignment?> RevokeAsync(string serviceName, string daemonId, long expectedGeneration, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var key = new AssignmentKey(serviceName, daemonId);
            if (!assignments.TryGetValue(key, out var existing) || existing.Released || existing.Generation != expectedGeneration)
            {
                return Task.FromResult<DaemonAssignment?>(null);
            }

            var row = existing with
            {
                Generation = existing.Generation + 1,
                State = DaemonAssignmentState.Revoking,
            };
            assignments[key] = row;
            return Task.FromResult<DaemonAssignment?>(ToAssignment(daemonId, row));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DaemonAssignment>> GetAllAssignmentsAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlyList<DaemonAssignment> result = assignments
                .Where(a => a.Key.ServiceName == serviceName && !a.Value.Released)
                .Select(a => ToAssignment(a.Key.DaemonId, a.Value))
                .ToList();
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DaemonAssignment>> GetNodeAssignmentsAsync(string serviceName, Guid nodeId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlyList<DaemonAssignment> result = assignments
                .Where(a => a.Key.ServiceName == serviceName && a.Value.AssignedNodeId == nodeId && !a.Value.Released)
                .Select(a => ToAssignment(a.Key.DaemonId, a.Value))
                .ToList();
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc />
    public Task<bool> AcknowledgeAsync(string serviceName, string daemonId, long generation, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var key = new AssignmentKey(serviceName, daemonId);
            if (!assignments.TryGetValue(key, out var existing)
                || existing.Released
                || existing.Generation != generation
                || existing.State != DaemonAssignmentState.Assigning)
            {
                return Task.FromResult(false);
            }

            assignments[key] = existing with { State = DaemonAssignmentState.Active };
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> ConfirmDrainAsync(string serviceName, string daemonId, long generation, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var key = new AssignmentKey(serviceName, daemonId);
            if (!assignments.TryGetValue(key, out var existing)
                || existing.Released
                || existing.Generation != generation
                || existing.State != DaemonAssignmentState.Revoking)
            {
                return Task.FromResult(false);
            }

            assignments[key] = Release(existing);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(string serviceName, string daemonId, long generation, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var key = new AssignmentKey(serviceName, daemonId);
            if (!assignments.TryGetValue(key, out var existing) || existing.Released || existing.Generation != generation)
            {
                return Task.FromResult(false);
            }

            assignments[key] = Release(existing);
            return Task.FromResult(true);
        }
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private static AssignmentRow Release(AssignmentRow row) =>
        row with { Generation = row.Generation + 1, Released = true };

    private static DaemonAssignment ToAssignment(string daemonId, AssignmentRow row) =>
        new(daemonId, row.AssignedNodeId, row.Generation, row.State, row.AssignedAt, row.FencingKey);

    private sealed record NodeRow(string ServiceName, string[] DaemonIds, Guid HeartbeatId, TimeSpan Ttl);

    private sealed record LeaseRow(Guid NodeId, Guid HeartbeatId, TimeSpan Duration);

    private readonly record struct AssignmentKey(string ServiceName, string DaemonId);

    private sealed record AssignmentRow(
        Guid AssignedNodeId,
        long Generation,
        DaemonAssignmentState State,
        DateTime AssignedAt,
        Guid FencingKey,
        bool Released = false);

    /// <summary>
    /// A node's connection to the store. While <see cref="IsPartitioned"/> is <c>true</c>, every
    /// operation throws, the way calls fail when a node cannot reach the database. While
    /// <see cref="LosesResponses"/> is <c>true</c>, every operation takes effect and then throws, the way calls
    /// fail when responses are lost or arrive after the query timeout.
    /// </summary>
    internal sealed class Connection(InMemoryCoordinationStore store) : ILeaderElectionPersistence, IDaemonAssignmentPersistence
    {
        /// <summary>
        /// Gets or sets a value indicating whether operations on this connection fail.
        /// </summary>
        public bool IsPartitioned { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether operations on this connection take effect but fail.
        /// </summary>
        public bool LosesResponses { get; set; }

        public Task<HeartbeatResult> HeartbeatAsync(HeartbeatRequest request, CancellationToken cancellationToken = default) =>
            Guard(() => store.HeartbeatAsync(request, cancellationToken));

        public Task ResignLeadershipAsync(
            string serviceName,
            Guid nodeId,
            IReadOnlyCollection<Guid> heartbeatIds,
            CancellationToken cancellationToken = default) =>
            Guard(() => store.ResignLeadershipAsync(serviceName, nodeId, heartbeatIds, cancellationToken));

        public Task<IReadOnlyList<Guid>> RemoveNodesAsync(
            string serviceName,
            IReadOnlyCollection<NodeRegistration> nodes,
            CancellationToken cancellationToken = default) =>
            Guard(() => store.RemoveNodesAsync(serviceName, nodes, cancellationToken));

        public Task DeregisterNodeAsync(Guid nodeId, CancellationToken cancellationToken = default) =>
            Guard(() => store.DeregisterNodeAsync(nodeId, cancellationToken));

        public Task<IReadOnlyList<NodeRegistration>> GetNodesAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Guard(() => store.GetNodesAsync(serviceName, cancellationToken));

        public Task<DaemonAssignment?> AssignAsync(string serviceName, string daemonId, Guid nodeId, DaemonAssignment? expected, CancellationToken cancellationToken = default) =>
            Guard(() => store.AssignAsync(serviceName, daemonId, nodeId, expected, cancellationToken));

        public Task<DaemonAssignment?> RevokeAsync(string serviceName, string daemonId, long expectedGeneration, CancellationToken cancellationToken = default) =>
            Guard(() => store.RevokeAsync(serviceName, daemonId, expectedGeneration, cancellationToken));

        public Task<IReadOnlyList<DaemonAssignment>> GetAllAssignmentsAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Guard(() => store.GetAllAssignmentsAsync(serviceName, cancellationToken));

        public Task<IReadOnlyList<DaemonAssignment>> GetNodeAssignmentsAsync(string serviceName, Guid nodeId, CancellationToken cancellationToken = default) =>
            Guard(() => store.GetNodeAssignmentsAsync(serviceName, nodeId, cancellationToken));

        public Task<bool> AcknowledgeAsync(string serviceName, string daemonId, long generation, CancellationToken cancellationToken = default) =>
            Guard(() => store.AcknowledgeAsync(serviceName, daemonId, generation, cancellationToken));

        public Task<bool> ConfirmDrainAsync(string serviceName, string daemonId, long generation, CancellationToken cancellationToken = default) =>
            Guard(() => store.ConfirmDrainAsync(serviceName, daemonId, generation, cancellationToken));

        public Task<bool> RemoveAsync(string serviceName, string daemonId, long generation, CancellationToken cancellationToken = default) =>
            Guard(() => store.RemoveAsync(serviceName, daemonId, generation, cancellationToken));

        private async Task<TResult> Guard<TResult>(Func<Task<TResult>> operation)
        {
            if (IsPartitioned)
            {
                throw new InvalidOperationException("Database unreachable.");
            }

            var result = await operation();
            return LosesResponses ? throw new TimeoutException("No response.") : result;
        }

        private Task Guard(Func<Task> operation) =>
            Guard(async () =>
            {
                await operation();
                return true;
            });
    }
}

namespace Emit.Daemon;

using Emit.Abstractions.LeaderElection;

/// <summary>
/// Tracks, on this node's own monotonic clock, how long each registered node of its service has gone without a new
/// heartbeat. A node is alive until its registration goes unrenewed for the registration TTL it requested, and
/// responsive while it renewed recently.
/// </summary>
/// <remarks>
/// No time the database records is involved, and no wall-clock time either: the monotonic clock never counts more
/// time than has passed, so no step in either clock can make a node look dead early, nor keep a dead one alive.
/// A heartbeat is first seen no earlier than the node wrote it, after the node started that heartbeat, so a node is
/// never judged dead before a registration TTL has passed since it started its last heartbeat, the moment its own
/// fencing and release are measured from. Every node of the service keeps observing, so a node that becomes leader
/// already knows how long each node has gone unrenewed; a node that has just started waits a full registration TTL.
/// </remarks>
internal sealed class NodeObservations(TimeProvider timeProvider)
{
    private Dictionary<Guid, (Guid HeartbeatId, long Since, bool Renewed)> seen = [];

    /// <summary>
    /// Records <paramref name="nodes"/>, read by a query issued at <paramref name="readStartedAt"/>, and returns
    /// how long each has gone unrenewed. A registration read unchanged shows the node had not renewed by the time
    /// the query was issued, not by the time its result is handled, so that is when the time is measured to; a
    /// newly seen heartbeat counts from now. Nodes that are no longer registered are forgotten. A registration
    /// replaced while the database read the service's nodes can be read twice; the later one is observed.
    /// </summary>
    public IReadOnlyList<ObservedNode> Observe(IReadOnlyList<NodeRegistration> nodes, long readStartedAt)
    {
        var now = timeProvider.GetTimestamp();
        var next = new Dictionary<Guid, (Guid HeartbeatId, long Since, bool Renewed)>(nodes.Count);
        List<ObservedNode> observed = [];
        foreach (var node in nodes.GroupBy(n => n.NodeId).Select(g => g.Last()))
        {
            var known = seen.TryGetValue(node.NodeId, out var previous);
            var entry = known && previous.HeartbeatId == node.HeartbeatId
                ? previous
                : (node.HeartbeatId, Since: now, Renewed: known);
            next[node.NodeId] = entry;
            var unrenewed = entry.Since < readStartedAt ? timeProvider.GetElapsedTime(entry.Since, readStartedAt) : TimeSpan.Zero;
            observed.Add(new ObservedNode(node, unrenewed, entry.Renewed));
        }

        seen = next;
        return observed;
    }
}

/// <summary>
/// A registered node, how long it has been seen to go without a new heartbeat, and whether it was seen renewing
/// its registration at all. A registration written once, such as by a heartbeat that reached the database after
/// its node shut down, is not seen renewing.
/// </summary>
internal sealed record ObservedNode(NodeRegistration Registration, TimeSpan Unrenewed, bool HasRenewed)
{
    /// <summary>
    /// Gets a value indicating whether the node's registration has not gone unrenewed for its TTL.
    /// </summary>
    public bool IsAlive => Unrenewed < Registration.RegistrationTtl;
}

/// <summary>
/// A live node the leader can place daemons on.
/// </summary>
/// <param name="NodeId">The unique identifier of the node.</param>
/// <param name="DaemonIds">The daemons the node declared.</param>
/// <param name="IsResponsive">
/// Whether the node renewed its registration recently enough for new work to be placed on it. A live node that
/// is not responsive keeps its daemons, but new ones go to responsive nodes first.
/// </param>
/// <param name="GaveUpADaemon">
/// Whether the node was seen, within the failure cooldown, no longer offering a daemon it ran, as a node does with
/// one it keeps failing. It may be unable to run any, so it gets new ones only when no other node can take them.
/// </param>
internal sealed record LiveNode(Guid NodeId, IReadOnlyList<string> DaemonIds, bool IsResponsive, bool GaveUpADaemon);

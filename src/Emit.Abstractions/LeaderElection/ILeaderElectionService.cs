namespace Emit.Abstractions.LeaderElection;

/// <summary>
/// Provides information about the current node's role in the leader election of its service.
/// </summary>
/// <remarks>
/// <para>
/// Each application instance participating in leader election is assigned a unique
/// <see cref="NodeId"/>. Each service elects its own leader: exactly one node of a service
/// holds the leader role at any given time, even when several services share a database.
/// </para>
/// <para>
/// Read <see cref="IsLeader"/> right before each leader-only action: it reports the end of leadership at once,
/// even after a pause or a suspend. <see cref="LeadershipToken"/> stops longer work, but is cancelled on the
/// thread pool and can fire late. Neither stops work already under way, so another node can lead while it
/// finishes; leader-only work whose effects must never overlap should be fenced in the system it writes to.
/// </para>
/// </remarks>
public interface ILeaderElectionService
{
    /// <summary>
    /// Gets the unique identifier for this node.
    /// </summary>
    Guid NodeId { get; }

    /// <summary>
    /// Gets a value indicating whether this node currently holds the leader role of its service.
    /// </summary>
    bool IsLeader { get; }

    /// <summary>
    /// Gets a cancellation token that is cancelled when this node loses leadership.
    /// </summary>
    /// <remarks>
    /// The token is reset each time this node becomes leader. When the node is not
    /// the leader, the token is already cancelled. Callbacks registered on it, and continuations of
    /// work awaiting it, run on the thread pool.
    /// </remarks>
    CancellationToken LeadershipToken { get; }
}

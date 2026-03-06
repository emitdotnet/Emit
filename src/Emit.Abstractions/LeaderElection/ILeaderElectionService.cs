namespace Emit.Abstractions.LeaderElection;

/// <summary>
/// Provides information about the current node's role in the leader election cluster.
/// </summary>
/// <remarks>
/// <para>
/// Each application instance participating in leader election is assigned a unique
/// <see cref="NodeId"/>. Exactly one node in the cluster holds the leader role at
/// any given time.
/// </para>
/// <para>
/// Use <see cref="LeadershipToken"/> to gate leader-only work — the token is cancelled
/// immediately when this node loses leadership.
/// </para>
/// </remarks>
public interface ILeaderElectionService
{
    /// <summary>
    /// Gets the unique identifier for this node.
    /// </summary>
    Guid NodeId { get; }

    /// <summary>
    /// Gets a value indicating whether this node currently holds the leader role.
    /// </summary>
    bool IsLeader { get; }

    /// <summary>
    /// Gets a cancellation token that is cancelled when this node loses leadership.
    /// </summary>
    /// <remarks>
    /// The token is reset each time this node becomes leader. When the node is not
    /// the leader, the token is already cancelled.
    /// </remarks>
    CancellationToken LeadershipToken { get; }
}

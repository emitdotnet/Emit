namespace Emit.Observability;

using System.ComponentModel;
using Emit.Abstractions.Observability;
using Microsoft.Extensions.Logging;

/// <summary>
/// Invokes all registered <see cref="ILeaderElectionObserver"/> instances for leader election
/// lifecycle events.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class LeaderElectionObserverInvoker(
    IEnumerable<ILeaderElectionObserver> observers,
    ILogger<LeaderElectionObserverInvoker> logger)
{
    private readonly ILeaderElectionObserver[] observers = observers.ToArray();

    /// <summary>
    /// Notifies observers that this node has been elected as leader.
    /// </summary>
    public Task OnLeaderElectedAsync(Guid nodeId, CancellationToken cancellationToken)
        => observers.InvokeAllAsync(o => o.OnLeaderElectedAsync(nodeId, cancellationToken), logger);

    /// <summary>
    /// Notifies observers that this node has lost the leader role.
    /// </summary>
    public Task OnLeaderLostAsync(Guid nodeId, CancellationToken cancellationToken)
        => observers.InvokeAllAsync(o => o.OnLeaderLostAsync(nodeId, cancellationToken), logger);

    /// <summary>
    /// Notifies observers that this node has registered itself in the cluster.
    /// </summary>
    public Task OnNodeRegisteredAsync(Guid nodeId, CancellationToken cancellationToken)
        => observers.InvokeAllAsync(o => o.OnNodeRegisteredAsync(nodeId, cancellationToken), logger);

    /// <summary>
    /// Notifies observers that a dead node has been removed from the cluster.
    /// </summary>
    public Task OnNodeRemovedAsync(Guid nodeId, CancellationToken cancellationToken)
        => observers.InvokeAllAsync(o => o.OnNodeRemovedAsync(nodeId, cancellationToken), logger);
}

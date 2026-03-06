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
    public async Task OnLeaderElectedAsync(Guid nodeId, CancellationToken cancellationToken)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnLeaderElectedAsync(nodeId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ILeaderElectionObserver.OnLeaderElectedAsync failed for {ObserverType}", observer.GetType().Name);
            }
        }
    }

    /// <summary>
    /// Notifies observers that this node has lost the leader role.
    /// </summary>
    public async Task OnLeaderLostAsync(Guid nodeId, CancellationToken cancellationToken)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnLeaderLostAsync(nodeId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ILeaderElectionObserver.OnLeaderLostAsync failed for {ObserverType}", observer.GetType().Name);
            }
        }
    }

    /// <summary>
    /// Notifies observers that this node has registered itself in the cluster.
    /// </summary>
    public async Task OnNodeRegisteredAsync(Guid nodeId, CancellationToken cancellationToken)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnNodeRegisteredAsync(nodeId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ILeaderElectionObserver.OnNodeRegisteredAsync failed for {ObserverType}", observer.GetType().Name);
            }
        }
    }

    /// <summary>
    /// Notifies observers that a dead node has been removed from the cluster.
    /// </summary>
    public async Task OnNodeRemovedAsync(Guid nodeId, CancellationToken cancellationToken)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnNodeRemovedAsync(nodeId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ILeaderElectionObserver.OnNodeRemovedAsync failed for {ObserverType}", observer.GetType().Name);
            }
        }
    }
}

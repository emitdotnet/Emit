namespace Emit.Observability;

using System.ComponentModel;
using Emit.Abstractions.Observability;
using Microsoft.Extensions.Logging;

/// <summary>
/// Invokes all registered <see cref="IDaemonObserver"/> instances for daemon assignment
/// lifecycle events.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class DaemonObserverInvoker(
    IEnumerable<IDaemonObserver> observers,
    ILogger<DaemonObserverInvoker> logger)
{
    private readonly IDaemonObserver[] observers = observers.ToArray();

    /// <summary>
    /// Notifies observers that a daemon has been assigned to a node.
    /// </summary>
    public async Task OnDaemonAssignedAsync(string daemonId, Guid nodeId, CancellationToken cancellationToken)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnDaemonAssignedAsync(daemonId, nodeId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "IDaemonObserver.OnDaemonAssignedAsync failed for {ObserverType}", observer.GetType().Name);
            }
        }
    }

    /// <summary>
    /// Notifies observers that a daemon has been started on this node.
    /// </summary>
    public async Task OnDaemonStartedAsync(string daemonId, Guid nodeId, CancellationToken cancellationToken)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnDaemonStartedAsync(daemonId, nodeId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "IDaemonObserver.OnDaemonStartedAsync failed for {ObserverType}", observer.GetType().Name);
            }
        }
    }

    /// <summary>
    /// Notifies observers that a daemon has been stopped on this node.
    /// </summary>
    public async Task OnDaemonStoppedAsync(string daemonId, Guid nodeId, CancellationToken cancellationToken)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnDaemonStoppedAsync(daemonId, nodeId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "IDaemonObserver.OnDaemonStoppedAsync failed for {ObserverType}", observer.GetType().Name);
            }
        }
    }

    /// <summary>
    /// Notifies observers that a daemon assignment has been revoked.
    /// </summary>
    public async Task OnDaemonRevokedAsync(string daemonId, Guid nodeId, CancellationToken cancellationToken)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnDaemonRevokedAsync(daemonId, nodeId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "IDaemonObserver.OnDaemonRevokedAsync failed for {ObserverType}", observer.GetType().Name);
            }
        }
    }
}

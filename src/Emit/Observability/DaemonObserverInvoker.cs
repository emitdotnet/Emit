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
    public Task OnDaemonAssignedAsync(string daemonId, Guid nodeId, CancellationToken cancellationToken)
        => observers.InvokeAllAsync(o => o.OnDaemonAssignedAsync(daemonId, nodeId, cancellationToken), logger);

    /// <summary>
    /// Notifies observers that a daemon has been started on this node.
    /// </summary>
    public Task OnDaemonStartedAsync(string daemonId, Guid nodeId, CancellationToken cancellationToken)
        => observers.InvokeAllAsync(o => o.OnDaemonStartedAsync(daemonId, nodeId, cancellationToken), logger);

    /// <summary>
    /// Notifies observers that a daemon has been stopped on this node.
    /// </summary>
    public Task OnDaemonStoppedAsync(string daemonId, Guid nodeId, CancellationToken cancellationToken)
        => observers.InvokeAllAsync(o => o.OnDaemonStoppedAsync(daemonId, nodeId, cancellationToken), logger);

    /// <summary>
    /// Notifies observers that a daemon assignment has been revoked.
    /// </summary>
    public Task OnDaemonRevokedAsync(string daemonId, Guid nodeId, CancellationToken cancellationToken)
        => observers.InvokeAllAsync(o => o.OnDaemonRevokedAsync(daemonId, nodeId, cancellationToken), logger);
}

namespace Emit.Observability;

using System.ComponentModel;
using Emit.Abstractions.Observability;
using Microsoft.Extensions.Logging;

/// <summary>
/// Notifies all registered <see cref="IDaemonObserver"/> instances of daemon assignment lifecycle events, in order
/// and away from the heartbeat.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class DaemonObserverInvoker
{
    private readonly IDaemonObserver[] observers;
    private readonly ObserverNotificationQueue notifications;
    private readonly ILogger<DaemonObserverInvoker> logger;

    /// <summary>
    /// Creates an invoker that notifies <paramref name="observers"/> on the thread pool.
    /// </summary>
    public DaemonObserverInvoker(
        IEnumerable<IDaemonObserver> observers,
        ILogger<DaemonObserverInvoker> logger)
        : this(observers, TaskScheduler.Default, logger)
    {
    }

    /// <summary>
    /// Creates an invoker that notifies <paramref name="observers"/> on <paramref name="scheduler"/>.
    /// </summary>
    internal DaemonObserverInvoker(
        IEnumerable<IDaemonObserver> observers,
        TaskScheduler scheduler,
        ILogger<DaemonObserverInvoker> logger)
    {
        ArgumentNullException.ThrowIfNull(observers);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(logger);

        this.observers = observers.ToArray();
        this.logger = logger;
        notifications = new ObserverNotificationQueue(scheduler, logger);
    }

    /// <summary>
    /// Notifies observers that a daemon has been assigned to a node.
    /// </summary>
    public void OnDaemonAssigned(string daemonId, Guid nodeId) =>
        Notify(o => o.OnDaemonAssignedAsync(daemonId, nodeId, CancellationToken.None));

    /// <summary>
    /// Notifies observers that a daemon has been started on this node.
    /// </summary>
    public void OnDaemonStarted(string daemonId, Guid nodeId) =>
        Notify(o => o.OnDaemonStartedAsync(daemonId, nodeId, CancellationToken.None));

    /// <summary>
    /// Notifies observers that a daemon has been stopped on this node.
    /// </summary>
    public void OnDaemonStopped(string daemonId, Guid nodeId) =>
        Notify(o => o.OnDaemonStoppedAsync(daemonId, nodeId, CancellationToken.None));

    /// <summary>
    /// Notifies observers that a daemon assignment has been revoked.
    /// </summary>
    public void OnDaemonRevoked(string daemonId, Guid nodeId) =>
        Notify(o => o.OnDaemonRevokedAsync(daemonId, nodeId, CancellationToken.None));

    private void Notify(Func<IDaemonObserver, Task> action)
    {
        if (observers.Length > 0)
        {
            notifications.Post(() => observers.InvokeAllAsync(action, logger));
        }
    }
}

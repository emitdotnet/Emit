namespace Emit.Observability;

using System.ComponentModel;
using Emit.Abstractions.Observability;
using Microsoft.Extensions.Logging;

/// <summary>
/// Notifies all registered <see cref="ILeaderElectionObserver"/> instances of leader election lifecycle events, in
/// order and away from the heartbeat.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class LeaderElectionObserverInvoker
{
    private readonly ILeaderElectionObserver[] observers;
    private readonly ObserverNotificationQueue notifications;
    private readonly ILogger<LeaderElectionObserverInvoker> logger;

    /// <summary>
    /// Creates an invoker that notifies <paramref name="observers"/> on the thread pool.
    /// </summary>
    public LeaderElectionObserverInvoker(
        IEnumerable<ILeaderElectionObserver> observers,
        ILogger<LeaderElectionObserverInvoker> logger)
        : this(observers, TaskScheduler.Default, logger)
    {
    }

    /// <summary>
    /// Creates an invoker that notifies <paramref name="observers"/> on <paramref name="scheduler"/>.
    /// </summary>
    internal LeaderElectionObserverInvoker(
        IEnumerable<ILeaderElectionObserver> observers,
        TaskScheduler scheduler,
        ILogger<LeaderElectionObserverInvoker> logger)
    {
        ArgumentNullException.ThrowIfNull(observers);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(logger);

        this.observers = observers.ToArray();
        this.logger = logger;
        notifications = new ObserverNotificationQueue(scheduler, logger);
    }

    /// <summary>
    /// Notifies observers that this node has been elected as leader.
    /// </summary>
    public void OnLeaderElected(Guid nodeId) => Notify(o => o.OnLeaderElectedAsync(nodeId, CancellationToken.None));

    /// <summary>
    /// Notifies observers that this node has lost the leader role.
    /// </summary>
    public void OnLeaderLost(Guid nodeId) => Notify(o => o.OnLeaderLostAsync(nodeId, CancellationToken.None));

    /// <summary>
    /// Notifies observers that this node has registered itself in the cluster.
    /// </summary>
    public void OnNodeRegistered(Guid nodeId) => Notify(o => o.OnNodeRegisteredAsync(nodeId, CancellationToken.None));

    /// <summary>
    /// Notifies observers that a dead node has been removed from the cluster.
    /// </summary>
    public void OnNodeRemoved(Guid nodeId) => Notify(o => o.OnNodeRemovedAsync(nodeId, CancellationToken.None));

    /// <summary>
    /// Waits, at most <paramref name="bound"/>, for the notifications raised so far to reach the observers.
    /// </summary>
    internal Task FlushAsync(TimeSpan bound, TimeProvider timeProvider) => notifications.FlushAsync(bound, timeProvider);

    private void Notify(Func<ILeaderElectionObserver, Task> action)
    {
        if (observers.Length > 0)
        {
            notifications.Post(() => observers.InvokeAllAsync(action, logger));
        }
    }
}

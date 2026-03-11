namespace Emit.Abstractions.Observability;

/// <summary>
/// Observes daemon assignment lifecycle events. Implement this interface for
/// monitoring, metrics, or integration test assertions.
/// </summary>
/// <remarks>
/// <para>
/// All methods have default implementations that return <see cref="Task.CompletedTask"/>,
/// so implementors only need to override the callbacks they care about.
/// </para>
/// <para>
/// Observer exceptions are caught and logged individually — a failing observer never
/// blocks other observers or interrupts daemon coordination.
/// </para>
/// </remarks>
public interface IDaemonObserver : IObserver
{
    /// <summary>
    /// Called when the leader assigns a daemon to a node.
    /// </summary>
    /// <param name="daemonId">The identifier of the assigned daemon.</param>
    /// <param name="nodeId">The node the daemon was assigned to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnDaemonAssignedAsync(string daemonId, Guid nodeId, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Called when a daemon has been started on this node.
    /// </summary>
    /// <param name="daemonId">The identifier of the started daemon.</param>
    /// <param name="nodeId">The node the daemon is running on.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnDaemonStartedAsync(string daemonId, Guid nodeId, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Called when a daemon has been stopped on this node.
    /// </summary>
    /// <param name="daemonId">The identifier of the stopped daemon.</param>
    /// <param name="nodeId">The node the daemon was running on.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnDaemonStoppedAsync(string daemonId, Guid nodeId, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Called when the leader revokes a daemon assignment from a node.
    /// </summary>
    /// <param name="daemonId">The identifier of the revoked daemon.</param>
    /// <param name="nodeId">The node the daemon was revoked from.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnDaemonRevokedAsync(string daemonId, Guid nodeId, CancellationToken cancellationToken) => Task.CompletedTask;
}

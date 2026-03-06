namespace Emit.Abstractions.Daemon;

/// <summary>
/// Defines a daemon that can be assigned to a cluster node by the leader.
/// Implement this interface to create work items that the leader automatically
/// distributes across available nodes.
/// </summary>
/// <remarks>
/// <para>
/// Each daemon is identified by a unique <see cref="DaemonId"/>. The leader assigns
/// exactly one node to run each daemon at any time. When a node receives an assignment,
/// <see cref="StartAsync"/> is called with a cancellation token that fires when the
/// assignment is revoked or the node loses its heartbeat.
/// </para>
/// <para>
/// Implementations must support being started and stopped multiple times across the
/// lifetime of the application — for example, when work is revoked and later reassigned
/// back to the same node.
/// </para>
/// </remarks>
public interface IDaemonAgent
{
    /// <summary>
    /// Gets the unique identifier for this daemon type.
    /// </summary>
    string DaemonId { get; }

    /// <summary>
    /// Starts the daemon. The implementation should begin its work and return promptly,
    /// running any long-lived processing on a background task.
    /// </summary>
    /// <param name="assignmentToken">
    /// Cancelled when the assignment is revoked or the node loses its heartbeat.
    /// The daemon must stop all work when this token fires.
    /// </param>
    /// <returns>A task that completes once the daemon has started.</returns>
    Task StartAsync(CancellationToken assignmentToken);

    /// <summary>
    /// Stops the daemon gracefully. Called when the assignment is being revoked
    /// or during application shutdown. The daemon should complete any in-flight
    /// work and release resources.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the stop operation.</param>
    /// <returns>A task that completes when the daemon has fully stopped.</returns>
    Task StopAsync(CancellationToken cancellationToken);
}

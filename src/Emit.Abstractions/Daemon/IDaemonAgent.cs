namespace Emit.Abstractions.Daemon;

/// <summary>
/// Defines a daemon that can be assigned to a node by the leader of its service.
/// Implement this interface to create work that runs on exactly one node of the service
/// at a time, and moves to another node when that node goes away.
/// </summary>
/// <remarks>
/// <para>
/// Each daemon is identified by a unique <see cref="DaemonId"/>. When a node receives the
/// assignment, <see cref="RunAsync"/> is called and runs until its token is cancelled: when the
/// assignment is revoked, when the node loses contact with the database, or on shutdown.
/// </para>
/// <para>
/// A run must end within <c>DrainTimeout</c> of its token being cancelled. One that does not is reported, and its
/// node stops offering the daemon until the run ends, so another node may run the daemon meanwhile.
/// </para>
/// <para>
/// Read <see cref="DaemonRunContext.HoldsAssignment"/> right before each side effect: it turns false at once when
/// the run must stop, even when the token is cancelled late. A run that is paused long enough, for example by a
/// stalled process, can still be going when another node already runs the daemon. Work whose effects must never
/// overlap should be fenced in the system it writes to, with <see cref="DaemonRunContext.FencingKey"/> and
/// <see cref="DaemonRunContext.Generation"/>.
/// </para>
/// <para>
/// A run that completes or throws before its token is cancelled is treated as a failure: the
/// daemon is started again, and after repeated failures it is moved to another node. A daemon
/// can be run many times over the lifetime of the application, but never twice at the same
/// time on the same node.
/// </para>
/// </remarks>
public interface IDaemonAgent
{
    /// <summary>
    /// Gets the unique identifier for this daemon type.
    /// </summary>
    string DaemonId { get; }

    /// <summary>
    /// Runs the daemon until <see cref="DaemonRunContext.AssignmentToken"/> is cancelled. The method is called on
    /// the thread pool.
    /// </summary>
    /// <param name="context">The assignment this run belongs to.</param>
    /// <returns>A task that completes when the daemon has stopped.</returns>
    Task RunAsync(DaemonRunContext context);
}

namespace Emit.Configuration;

/// <summary>
/// Configuration options for daemon assignment coordination.
/// </summary>
public sealed class DaemonOptions
{
    /// <summary>
    /// Gets or sets how long a node has to acknowledge an assignment before the leader
    /// considers it stuck and reassigns.
    /// </summary>
    public TimeSpan AcknowledgeTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long a daemon may take to stop after it is cancelled. Keep the host's shutdown timeout and
    /// any orchestrator grace period comfortably longer than this, so a node that shuts down has time to hand its
    /// daemons over; the host's default of 30 seconds leaves room with the defaults. Must be less than
    /// <see cref="LeaderElectionOptions.NodeRegistrationTtl"/> minus <see cref="LeaderElectionOptions.LeaseDuration"/>
    /// minus <see cref="LeaderElectionOptions.HeartbeatInterval"/>.
    /// </summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Gets or sets how many times in a row a daemon may fail on a node before that node stops
    /// running it for <see cref="FailureCooldown"/>, so it is assigned to another node.
    /// </summary>
    /// <remarks>
    /// A daemon fails when its run ends or throws before it is asked to stop. When no other node can take the
    /// daemon, the node keeps retrying it.
    /// </remarks>
    public int MaxConsecutiveFailures { get; set; } = 3;

    /// <summary>
    /// Gets or sets how long a node stops running a daemon after it failed
    /// <see cref="MaxConsecutiveFailures"/> times in a row. Must be greater than 4 x
    /// (<see cref="LeaderElectionOptions.HeartbeatInterval"/> + 3 x <see cref="LeaderElectionOptions.QueryTimeout"/>).
    /// </summary>
    public TimeSpan FailureCooldown { get; set; } = TimeSpan.FromMinutes(5);
}

namespace Emit.Configuration;

/// <summary>
/// Configuration options for leader election and node registration.
/// </summary>
public sealed class LeaderElectionOptions
{
    /// <summary>
    /// Gets or sets the interval at which each node sends a heartbeat and attempts leader election.
    /// </summary>
    /// <remarks>
    /// Must be greater than <see cref="QueryTimeout"/> and less than <see cref="LeaseDuration"/>
    /// minus <see cref="QueryTimeout"/> to ensure reliable leader renewal.
    /// </remarks>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Gets or sets the duration of the leader lease. The leader must renew the lease
    /// before it expires or another node may take over.
    /// </summary>
    /// <remarks>
    /// Must be greater than <see cref="HeartbeatInterval"/> plus <see cref="QueryTimeout"/>
    /// to prevent premature lease expiry during normal operation.
    /// </remarks>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the maximum time allowed for a single database query during heartbeat.
    /// </summary>
    /// <remarks>
    /// Must be less than <see cref="HeartbeatInterval"/>. If a heartbeat query exceeds this
    /// duration, the node assumes it lost connectivity and immediately drops leadership.
    /// </remarks>
    public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the time-to-live for node registration records. Nodes with a last-seen
    /// time older than this duration are considered dead and removed by the leader.
    /// </summary>
    /// <remarks>
    /// Must be greater than <see cref="LeaseDuration"/> to ensure dead node cleanup
    /// does not remove nodes that are still actively participating.
    /// </remarks>
    public TimeSpan NodeRegistrationTtl { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Gets or sets a human-readable identifier for this node instance.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="Environment.MachineName"/> when <c>null</c>.
    /// </remarks>
    public string? InstanceId { get; set; }
}

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
    /// Gets or sets the maximum time allowed for a single database query of leader election or daemon
    /// coordination.
    /// </summary>
    /// <remarks>
    /// <see cref="HeartbeatInterval"/> must be more than six times this long. At least 100 milliseconds.
    /// </remarks>
    public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets the longest a heartbeat may take: one query timeout for each database call it makes.
    /// </summary>
    internal TimeSpan HeartbeatTimeout => QueryTimeout * ValidationConstants.QueriesPerHeartbeat;

    /// <summary>
    /// Gets the lease as the database records it, in whole milliseconds, the precision every provider keeps.
    /// </summary>
    internal TimeSpan RecordedLease => TimeSpan.FromMilliseconds(Math.Floor(LeaseDuration.TotalMilliseconds));

    /// <summary>
    /// Gets how long a node holds its lease after the heartbeat that renewed it started: the recorded lease, ended
    /// early by the clock drift allowed, since other nodes wait the recorded lease on their own clocks.
    /// </summary>
    internal TimeSpan HeldLease => RecordedLease - (RecordedLease * ValidationConstants.MaxClockDriftRate);

    /// <summary>
    /// Gets how long a node goes without a successful heartbeat before it gives up its daemons: the recorded
    /// registration TTL, ended early by the clock drift allowed, since the leader waits it on its own clock.
    /// </summary>
    internal TimeSpan ReleaseAfter => RecordedRegistrationTtl - (RecordedRegistrationTtl * ValidationConstants.MaxClockDriftRate);

    /// <summary>
    /// Gets the registration TTL as the database records it, in whole milliseconds, the precision every provider keeps.
    /// </summary>
    internal TimeSpan RecordedRegistrationTtl => TimeSpan.FromMilliseconds(Math.Floor(NodeRegistrationTtl.TotalMilliseconds));

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
    /// Defaults to <see cref="Environment.MachineName"/> when <c>null</c>. At most 200 characters.
    /// </remarks>
    public string? InstanceId { get; set; }

    /// <summary>
    /// Gets or sets the name of the service this node belongs to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every replica of the same application must use the same name. Each service elects its own
    /// leader, assigns its own daemons to its own nodes, and processes only the outbox entries it
    /// enqueued, so several services can share one database.
    /// </para>
    /// <para>
    /// Defaults to the host's application name (the name of the entry assembly) when <c>null</c>. Two
    /// applications whose entry assemblies have the same name merge into one service when they share a
    /// database, and must set different names. Keep the name stable across deployments: outbox entries
    /// enqueued under a previous name are not processed by a service that was renamed.
    /// </para>
    /// </remarks>
    public string? ServiceName { get; set; }
}

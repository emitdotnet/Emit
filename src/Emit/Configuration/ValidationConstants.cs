namespace Emit.Configuration;

/// <summary>
/// Shared constants for configuration validation.
/// </summary>
public static class ValidationConstants
{
    /// <summary>
    /// Maximum allowed batch size for outbox operations.
    /// </summary>
    public const int MaxBatchSize = 10000;

    /// <summary>
    /// Minimum allowed polling interval.
    /// </summary>
    public static readonly TimeSpan MinPollingInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Minimum allowed lock cleanup interval.
    /// </summary>
    public static readonly TimeSpan MinLockCleanupInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Maximum length of a service name.
    /// </summary>
    public const int MaxServiceNameLength = 200;

    /// <summary>
    /// Maximum length of an instance identifier.
    /// </summary>
    public const int MaxInstanceIdLength = 200;

    /// <summary>
    /// Maximum length of a daemon identifier.
    /// </summary>
    public const int MaxDaemonIdLength = 200;

    /// <summary>
    /// Prefix reserved for the identifiers of Emit's own daemons, such as <c>emit:outbox</c>, in any letter case.
    /// </summary>
    public const string ReservedDaemonIdPrefix = "emit:";

    /// <summary>
    /// How many database calls a heartbeat makes at most: the lease compare-and-swap, the read of a lease another
    /// node holds, and the registration. A heartbeat is given that many query timeouts.
    /// </summary>
    public const int QueriesPerHeartbeat = 3;

    /// <summary>
    /// How much faster than another node's clock a node's clock is allowed to run, as a fraction: 1/1000, ten times
    /// what NTP keeps clocks within. A node gives up what it holds this fraction early, so it never outlasts what
    /// another node waits before taking it over.
    /// </summary>
    public const double MaxClockDriftRate = 0.001;

    /// <summary>
    /// How many query timeouts a heartbeat interval must exceed: the heartbeat itself, the two reads the leader
    /// places daemons from, and one write, so a database that answers every query within the timeout never keeps
    /// a heartbeat from getting work done.
    /// </summary>
    public const int QueryTimeoutsPerHeartbeat = QueriesPerHeartbeat + 3;

    /// <summary>
    /// Minimum query timeout: below it no networked database answers reliably, so every heartbeat would fail and
    /// no node could lead or run daemons.
    /// </summary>
    public static readonly TimeSpan MinQueryTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Minimum heartbeat interval. Timers can fire an operating system timer tick late (about 16 ms on Windows),
    /// and a node's lease keeps one heartbeat interval in reserve for its own timer, which must stay well above it.
    /// </summary>
    public static readonly TimeSpan MinHeartbeatInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum value of a leader election or daemon duration: the longest a timer can wait (about 49.7 days).
    /// </summary>
    public static readonly TimeSpan MaxCoordinationDuration = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
}

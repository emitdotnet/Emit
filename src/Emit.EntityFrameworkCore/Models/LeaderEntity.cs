namespace Emit.EntityFrameworkCore.Models;

/// <summary>
/// Represents the leader lease of a service in PostgreSQL. Each service has one row,
/// identified by its service name.
/// </summary>
internal sealed class LeaderEntity
{
    /// <summary>
    /// Gets or sets the name of the service this lease belongs to (primary key).
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the unique identifier of the current leader node.
    /// </summary>
    public Guid NodeId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the heartbeat that last acquired or renewed the lease.
    /// </summary>
    public Guid HeartbeatId { get; set; }

    /// <summary>
    /// Gets or sets how long the lease lasts without renewal, as its holder requested.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; }
}

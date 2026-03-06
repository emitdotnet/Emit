namespace Emit.EntityFrameworkCore.Models;

/// <summary>
/// Represents the leader election row in PostgreSQL. A single row with a
/// fixed key tracks the current leader.
/// </summary>
internal sealed class LeaderEntity
{
    /// <summary>
    /// Gets or sets the row key (primary key). Always <c>"leader"</c>.
    /// </summary>
    public string Key { get; set; } = "leader";

    /// <summary>
    /// Gets or sets the unique identifier of the current leader node.
    /// </summary>
    public Guid NodeId { get; set; }

    /// <summary>
    /// Gets or sets when the leader lease expires (UTC).
    /// </summary>
    public DateTime ExpiresAt { get; set; }
}

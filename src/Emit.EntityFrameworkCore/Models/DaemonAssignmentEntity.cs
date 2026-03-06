namespace Emit.EntityFrameworkCore.Models;

/// <summary>
/// Represents a daemon assignment row in PostgreSQL.
/// </summary>
internal sealed class DaemonAssignmentEntity
{
    /// <summary>
    /// Gets or sets the daemon identifier (primary key).
    /// </summary>
    public string DaemonId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the node this daemon is assigned to.
    /// </summary>
    public Guid AssignedNodeId { get; set; }

    /// <summary>
    /// Gets or sets the fencing generation number.
    /// </summary>
    public long Generation { get; set; }

    /// <summary>
    /// Gets or sets the assignment state.
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the assignment was made (UTC).
    /// </summary>
    public DateTime AssignedAt { get; set; }

    /// <summary>
    /// Gets or sets the drain deadline (UTC), set when state is revoking.
    /// </summary>
    public DateTime? DrainDeadline { get; set; }
}

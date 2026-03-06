namespace Emit.MongoDB.Models;

/// <summary>
/// Represents a daemon assignment in MongoDB.
/// </summary>
internal sealed class DaemonAssignmentDocument
{
    /// <summary>
    /// Gets or sets the daemon identifier (mapped as <c>_id</c>).
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

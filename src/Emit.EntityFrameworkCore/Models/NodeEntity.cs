namespace Emit.EntityFrameworkCore.Models;

/// <summary>
/// Represents a registered node in the leader election cluster.
/// </summary>
internal sealed class NodeEntity
{
    /// <summary>
    /// Gets or sets the unique identifier of the node (primary key).
    /// </summary>
    public Guid NodeId { get; set; }

    /// <summary>
    /// Gets or sets the human-readable identifier of the node instance.
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the node first started (UTC).
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// Gets or sets when the node last sent a heartbeat (UTC).
    /// </summary>
    public DateTime LastSeenAt { get; set; }
}

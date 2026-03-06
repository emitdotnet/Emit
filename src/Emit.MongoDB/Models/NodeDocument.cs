namespace Emit.MongoDB.Models;

/// <summary>
/// Represents a registered node in the leader election cluster.
/// </summary>
internal sealed class NodeDocument
{
    /// <summary>
    /// Gets or sets the unique identifier of the node (mapped as <c>_id</c>).
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

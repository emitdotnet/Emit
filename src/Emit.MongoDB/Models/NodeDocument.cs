namespace Emit.MongoDB.Models;

/// <summary>
/// Represents a registered node of a service.
/// </summary>
internal sealed class NodeDocument
{
    /// <summary>
    /// Gets or sets the unique identifier of the node (mapped as <c>_id</c>).
    /// </summary>
    public Guid NodeId { get; set; }

    /// <summary>
    /// Gets or sets the name of the service the node belongs to.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable identifier of the node instance.
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the identifiers of the daemons the node is able to run.
    /// </summary>
    public List<string> DaemonIds { get; set; } = [];

    /// <summary>
    /// Gets or sets when the node first started (UTC), for operators.
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// Gets or sets when the node last sent a heartbeat (UTC), for operators. Liveness is judged from
    /// <see cref="HeartbeatId"/>, never from this time.
    /// </summary>
    public DateTime LastSeenAt { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the node's last heartbeat.
    /// </summary>
    public Guid HeartbeatId { get; set; }

    /// <summary>
    /// Gets or sets how long the registration lasts without a heartbeat, as the node requested.
    /// </summary>
    public TimeSpan RegistrationTtl { get; set; }
}

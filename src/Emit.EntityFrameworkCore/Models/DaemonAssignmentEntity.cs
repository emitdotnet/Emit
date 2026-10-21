namespace Emit.EntityFrameworkCore.Models;

/// <summary>
/// Represents the assignment of a service's daemon in PostgreSQL. The service name and
/// daemon identifier form the primary key.
/// </summary>
internal sealed class DaemonAssignmentEntity
{
    /// <summary>
    /// Gets or sets the name of the service the daemon belongs to.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the daemon identifier.
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
    /// Gets or sets the fencing key, written when the row is created and never changed.
    /// </summary>
    public Guid FencingKey { get; set; }

    /// <summary>
    /// The state of a released assignment: no node runs the daemon, and the row only keeps its generation
    /// so that generations never repeat.
    /// </summary>
    internal const string ReleasedState = "Released";
}

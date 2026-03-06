namespace Emit.MongoDB.Models;

/// <summary>
/// Represents the leader election document in MongoDB. A single document with a
/// fixed key tracks the current leader.
/// </summary>
internal sealed class LeaderDocument
{
    /// <summary>
    /// Gets or sets the document key (mapped as <c>_id</c>). Always <c>"leader"</c>.
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

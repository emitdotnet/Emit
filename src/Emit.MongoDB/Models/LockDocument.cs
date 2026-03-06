namespace Emit.MongoDB.Models;

/// <summary>
/// Represents a distributed lock document in MongoDB.
/// </summary>
internal sealed class LockDocument
{
    /// <summary>
    /// Gets or sets the lock key (mapped as <c>_id</c>).
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the unique identifier of the current lock holder.
    /// </summary>
    public Guid LockId { get; set; }

    /// <summary>
    /// Gets or sets when the lock expires (UTC).
    /// </summary>
    public DateTime ExpiresAt { get; set; }
}

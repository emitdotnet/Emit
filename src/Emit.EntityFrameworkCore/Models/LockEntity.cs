namespace Emit.EntityFrameworkCore.Models;

/// <summary>
/// Represents a distributed lock row in PostgreSQL.
/// </summary>
internal sealed class LockEntity
{
    /// <summary>
    /// Gets or sets the lock key (primary key).
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

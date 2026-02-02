namespace Emit.Persistence.PostgreSQL.Models;

/// <summary>
/// Entity for tracking per-group sequence counters.
/// </summary>
/// <remarks>
/// This entity provides MongoDB-compatible sequence generation using
/// atomic counter increments with INSERT...ON CONFLICT (upsert).
/// </remarks>
internal sealed class SequenceCounter
{
    /// <summary>
    /// Gets or sets the group key (primary key).
    /// </summary>
    public required string GroupKey { get; set; }

    /// <summary>
    /// Gets or sets the current sequence value for this group.
    /// </summary>
    public long Sequence { get; set; }
}

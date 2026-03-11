namespace Emit.MongoDB.Models;

/// <summary>
/// Represents a sequence counter document for atomic sequence generation.
/// </summary>
/// <remarks>
/// Each document uses a well-known identifier as its <c>_id</c> field (e.g. <c>"emit.outbox"</c>).
/// A single document provides a globally monotonic counter via atomic <c>$inc</c>.
/// </remarks>
internal sealed class SequenceCounter
{
    /// <summary>
    /// Gets or sets the well-known identifier (used as the document _id).
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the current sequence value.
    /// </summary>
    public long Sequence { get; set; }
}

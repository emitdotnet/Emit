namespace Emit.MongoDB.Models;

/// <summary>
/// Represents a sequence counter document for atomic sequence generation.
/// </summary>
/// <remarks>
/// Each document tracks the current sequence number for a specific GroupKey.
/// The _id field is the GroupKey value.
/// </remarks>
internal sealed class SequenceCounter
{
    /// <summary>
    /// Gets or sets the GroupKey (used as the document _id).
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the current sequence value.
    /// </summary>
    public long Sequence { get; set; }
}

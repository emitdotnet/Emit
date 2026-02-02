namespace Emit.Models;

/// <summary>
/// Represents the processing status of an outbox entry.
/// </summary>
public enum OutboxStatus
{
    /// <summary>
    /// The entry is waiting to be processed.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The entry was successfully processed.
    /// </summary>
    Completed = 1,

    /// <summary>
    /// The entry failed to process and may be retried based on the retry policy.
    /// </summary>
    Failed = 2
}

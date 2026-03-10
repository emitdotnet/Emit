namespace Emit.Kafka;

/// <summary>
/// Specifies the cleanup policy for a Kafka topic.
/// </summary>
public enum TopicCleanupPolicy
{
    /// <summary>
    /// Old segments are deleted when their retention time or size limit is reached.
    /// </summary>
    Delete,

    /// <summary>
    /// Old segments are compacted, retaining only the latest value for each key.
    /// </summary>
    Compact,

    /// <summary>
    /// Both delete and compact policies are active.
    /// </summary>
    DeleteAndCompact,
}

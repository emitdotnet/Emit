namespace Emit.Kafka;

/// <summary>
/// Specifies the compression codec for a Kafka topic.
/// </summary>
public enum TopicCompressionType
{
    /// <summary>
    /// The broker preserves the compression codec set by the producer.
    /// </summary>
    Producer,

    /// <summary>
    /// No compression.
    /// </summary>
    Uncompressed,

    /// <summary>
    /// Gzip compression.
    /// </summary>
    Gzip,

    /// <summary>
    /// Snappy compression.
    /// </summary>
    Snappy,

    /// <summary>
    /// LZ4 compression.
    /// </summary>
    Lz4,

    /// <summary>
    /// Zstandard compression.
    /// </summary>
    Zstd,
}

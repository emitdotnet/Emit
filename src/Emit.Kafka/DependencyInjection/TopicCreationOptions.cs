namespace Emit.Kafka.DependencyInjection;

using System.Globalization;
using Confluent.Kafka.Admin;

/// <summary>
/// Configures how a Kafka topic is created when auto-provisioning is enabled.
/// Properties map to Kafka topic-level configuration entries.
/// </summary>
public sealed class TopicCreationOptions
{
    /// <summary>
    /// Number of partitions. <c>null</c> uses the broker default.
    /// </summary>
    public int? NumPartitions { get; set; }

    /// <summary>
    /// Replication factor. <c>null</c> uses the broker default.
    /// </summary>
    public short? ReplicationFactor { get; set; }

    /// <summary>
    /// How long messages are retained. <c>null</c> means infinite retention.
    /// </summary>
    public TimeSpan? Retention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Maximum size in bytes a partition can grow to before old segments are discarded.
    /// <c>null</c> means no size limit.
    /// </summary>
    public long? RetentionBytes { get; set; }

    /// <summary>
    /// The cleanup policy for the topic.
    /// </summary>
    public TopicCleanupPolicy CleanupPolicy { get; set; } = TopicCleanupPolicy.Delete;

    /// <summary>
    /// How long delete tombstone markers are retained for compacted topics.
    /// </summary>
    public TimeSpan DeleteRetention { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// The compression codec for the topic.
    /// </summary>
    public TopicCompressionType CompressionType { get; set; } = TopicCompressionType.Producer;

    /// <summary>
    /// Gzip compression level (1–9, or -1 for default).
    /// </summary>
    public int GzipCompressionLevel { get; set; } = -1;

    /// <summary>
    /// LZ4 compression level (1–17).
    /// </summary>
    public int Lz4CompressionLevel { get; set; } = 9;

    /// <summary>
    /// Zstandard compression level (negative values for fast mode, 1–22 for normal).
    /// </summary>
    public int ZstdCompressionLevel { get; set; } = 3;

    /// <summary>
    /// Minimum ratio of dirty log to total log for the compactor to run.
    /// </summary>
    public double MinCleanableDirtyRatio { get; set; } = 0.5;

    /// <summary>
    /// Minimum time a message remains uncompacted. <see cref="TimeSpan.Zero"/> means no delay.
    /// </summary>
    public TimeSpan MinCompactionLag { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Maximum time a message can remain uncompacted. <c>null</c> means no maximum.
    /// </summary>
    public TimeSpan? MaxCompactionLag { get; set; }

    /// <summary>
    /// Builds a Confluent <see cref="TopicSpecification"/> from these options.
    /// </summary>
    /// <param name="topicName">The topic name.</param>
    internal TopicSpecification BuildSpecification(string topicName)
    {
        var inv = CultureInfo.InvariantCulture;
        var configs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["retention.ms"] = Retention.HasValue
                ? ((long)Retention.Value.TotalMilliseconds).ToString(inv)
                : "-1",
            ["retention.bytes"] = RetentionBytes.HasValue
                ? RetentionBytes.Value.ToString(inv)
                : "-1",
            ["cleanup.policy"] = CleanupPolicy switch
            {
                TopicCleanupPolicy.Delete => "delete",
                TopicCleanupPolicy.Compact => "compact",
                TopicCleanupPolicy.DeleteAndCompact => "delete,compact",
                _ => "delete",
            },
            ["delete.retention.ms"] = ((long)DeleteRetention.TotalMilliseconds).ToString(inv),
            ["compression.type"] = CompressionType switch
            {
                TopicCompressionType.Producer => "producer",
                TopicCompressionType.Uncompressed => "uncompressed",
                TopicCompressionType.Gzip => "gzip",
                TopicCompressionType.Snappy => "snappy",
                TopicCompressionType.Lz4 => "lz4",
                TopicCompressionType.Zstd => "zstd",
                _ => "producer",
            },
            ["min.cleanable.dirty.ratio"] = MinCleanableDirtyRatio.ToString("F2", inv),
            ["min.compaction.lag.ms"] = ((long)MinCompactionLag.TotalMilliseconds).ToString(inv),
        };

        // Compression levels
        switch (CompressionType)
        {
            case TopicCompressionType.Gzip:
                configs["compression.gzip.level"] = GzipCompressionLevel.ToString(inv);
                break;
            case TopicCompressionType.Lz4:
                configs["compression.lz4.level"] = Lz4CompressionLevel.ToString(inv);
                break;
            case TopicCompressionType.Zstd:
                configs["compression.zstd.level"] = ZstdCompressionLevel.ToString(inv);
                break;
        }

        if (MaxCompactionLag.HasValue)
        {
            configs["max.compaction.lag.ms"] = ((long)MaxCompactionLag.Value.TotalMilliseconds).ToString(inv);
        }

        return new TopicSpecification
        {
            Name = topicName,
            NumPartitions = NumPartitions ?? -1,
            ReplicationFactor = ReplicationFactor ?? -1,
            Configs = configs,
        };
    }
}

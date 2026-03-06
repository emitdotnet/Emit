namespace Emit.Kafka.Consumer;

using System.Collections.ObjectModel;

/// <summary>
/// Default implementation of <see cref="IKafkaFeature"/>.
/// </summary>
internal sealed class KafkaFeature(string topic, int partition, long offset) : IKafkaFeature
{
    private ReadOnlyDictionary<string, string>? properties;

    /// <inheritdoc />
    public string Topic => topic;

    /// <inheritdoc />
    public int Partition => partition;

    /// <inheritdoc />
    public long Offset => offset;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Properties =>
        properties ??= new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
        {
            ["topic"] = topic,
            ["partition"] = partition.ToString(),
            ["offset"] = offset.ToString(),
        });
}

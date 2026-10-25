namespace Emit.Kafka.Metrics;

/// <summary>
/// The names of the meters the Kafka provider records its metrics on.
/// </summary>
public static class KafkaMeterNames
{
    /// <summary>
    /// Kafka producer and consumer metrics.
    /// </summary>
    public const string Kafka = "Emit.Kafka";

    /// <summary>
    /// librdkafka client and broker statistics.
    /// </summary>
    public const string Broker = "Emit.Kafka.Broker";
}

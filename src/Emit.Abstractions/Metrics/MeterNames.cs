namespace Emit.Abstractions.Metrics;

/// <summary>
/// Constants for all Emit meter names. Use these when subscribing to Emit meters
/// with OpenTelemetry or <see cref="System.Diagnostics.Metrics.MeterListener"/>.
/// </summary>
public static class MeterNames
{
    /// <summary>
    /// Pipeline and consumer middleware metrics.
    /// </summary>
    public const string Emit = "Emit";

    /// <summary>
    /// Outbox entry lifecycle and worker health metrics.
    /// </summary>
    public const string EmitOutbox = "Emit.Outbox";

    /// <summary>
    /// Distributed locking metrics.
    /// </summary>
    public const string EmitLock = "Emit.Lock";

    /// <summary>
    /// Mediator request dispatch metrics.
    /// </summary>
    public const string EmitMediator = "Emit.Mediator";

    /// <summary>
    /// Kafka-specific producer and consumer metrics.
    /// </summary>
    public const string EmitKafka = "Emit.Kafka";

    /// <summary>
    /// librdkafka client and broker statistics metrics.
    /// </summary>
    public const string EmitKafkaBroker = "Emit.Kafka.Broker";
}

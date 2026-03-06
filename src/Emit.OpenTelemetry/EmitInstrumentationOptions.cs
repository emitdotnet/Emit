namespace Emit.OpenTelemetry;

/// <summary>
/// Options for configuring Emit instrumentation with OpenTelemetry.
/// Controls which meters are subscribed and provides static tag enrichment.
/// </summary>
public sealed class EmitInstrumentationOptions
{
    private readonly List<KeyValuePair<string, object?>> tags = [];

    /// <summary>
    /// Gets or sets whether the <c>Emit</c> meter (pipeline and consumer middleware) is enabled. Default: <c>true</c>.
    /// </summary>
    public bool EnableEmitMeter { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the <c>Emit.Outbox</c> meter (outbox lifecycle and worker health) is enabled. Default: <c>true</c>.
    /// </summary>
    public bool EnableOutboxMeter { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the <c>Emit.Lock</c> meter (distributed locking) is enabled. Default: <c>true</c>.
    /// </summary>
    public bool EnableLockMeter { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the <c>Emit.Mediator</c> meter (mediator request dispatch) is enabled. Default: <c>true</c>.
    /// </summary>
    public bool EnableMediatorMeter { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the <c>Emit.Kafka</c> meter (Kafka producer/consumer) is enabled. Default: <c>true</c>.
    /// </summary>
    public bool EnableKafkaMeter { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the <c>Emit.Kafka.Broker</c> meter (librdkafka broker statistics) is enabled. Default: <c>true</c>.
    /// </summary>
    public bool EnableKafkaBrokerMeter { get; set; } = true;

    /// <summary>
    /// Adds a static tag that will be appended to every metric recorded by Emit.
    /// Tags are applied in the order they are added.
    /// </summary>
    /// <param name="key">The tag key.</param>
    /// <param name="value">The tag value.</param>
    /// <returns>This options instance for method chaining.</returns>
    public EmitInstrumentationOptions EnrichWithTag(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        tags.Add(new(key, value));
        return this;
    }

    internal ReadOnlyMemory<KeyValuePair<string, object?>> GetTags() => tags.ToArray();
}

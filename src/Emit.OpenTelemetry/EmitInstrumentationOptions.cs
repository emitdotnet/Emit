namespace Emit.OpenTelemetry;

/// <summary>
/// Options for configuring Emit instrumentation with OpenTelemetry.
/// Controls which meters are subscribed and provides static tag enrichment.
/// </summary>
public sealed class EmitInstrumentationOptions
{
    private readonly List<KeyValuePair<string, object?>> tags = [];
    private readonly HashSet<string> disabledMeters = new(StringComparer.Ordinal);

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
    /// Gets or sets whether the <c>Emit.Daemon</c> meter (daemon runs, starts and failures) is enabled. Default: <c>true</c>.
    /// </summary>
    public bool EnableDaemonMeter { get; set; } = true;

    /// <summary>
    /// Stops subscribing to a meter an optional Emit package registers, such as the broker statistics of the Kafka
    /// provider or the mediator's. Every package meter is subscribed to otherwise.
    /// </summary>
    /// <param name="meterName">The name of the package meter.</param>
    /// <returns>This options instance for method chaining.</returns>
    public EmitInstrumentationOptions DisableMeter(string meterName)
    {
        ArgumentException.ThrowIfNullOrEmpty(meterName);
        disabledMeters.Add(meterName);
        return this;
    }

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

    internal bool IsDisabled(string meterName) => disabledMeters.Contains(meterName);
}

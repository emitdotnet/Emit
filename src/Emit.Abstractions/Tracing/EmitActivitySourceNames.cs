namespace Emit.Abstractions.Tracing;

/// <summary>
/// Provides constant names for Emit ActivitySources.
/// </summary>
/// <remarks>
/// Use these constants when configuring OpenTelemetry to listen to Emit traces.
/// </remarks>
public static class EmitActivitySourceNames
{
    /// <summary>
    /// ActivitySource name for outbox operations (enqueue, process).
    /// </summary>
    /// <remarks>
    /// Value: "Emit.Outbox"
    /// </remarks>
    public const string Outbox = "Emit.Outbox";

    /// <summary>
    /// ActivitySource name for consumer operations (consume, DLQ replay).
    /// </summary>
    /// <remarks>
    /// Value: "Emit.Consumer"
    /// </remarks>
    public const string Consumer = "Emit.Consumer";

    /// <summary>
    /// Prefix for provider-specific ActivitySource names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Value: "Emit.Provider."
    /// </para>
    /// <para>
    /// Provider sources follow the pattern: "Emit.Provider.{ProviderId}"
    /// </para>
    /// <para>
    /// Example: "Emit.Provider.kafka"
    /// </para>
    /// </remarks>
    public const string ProviderPrefix = "Emit.Provider.";

    /// <summary>
    /// Gets the ActivitySource name for a specific provider.
    /// </summary>
    /// <param name="providerId">The provider identifier (e.g., "kafka").</param>
    /// <returns>The ActivitySource name (e.g., "Emit.Provider.kafka").</returns>
    public static string ForProvider(string providerId) => $"{ProviderPrefix}{providerId}";
}

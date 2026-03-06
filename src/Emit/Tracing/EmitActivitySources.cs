namespace Emit.Tracing;

using System.Collections.Concurrent;
using System.Diagnostics;
using Emit.Abstractions.Tracing;

/// <summary>
/// Provides singleton ActivitySource instances for Emit operations.
/// </summary>
internal static class EmitActivitySources
{
    private const string Version = "1.0.0";

    private static readonly ConcurrentDictionary<string, ActivitySource> ProviderSources = new();

    /// <summary>
    /// Gets the ActivitySource for outbox operations (enqueue, process).
    /// </summary>
    public static readonly ActivitySource Outbox = new(EmitActivitySourceNames.Outbox, Version);

    /// <summary>
    /// Gets the ActivitySource for consumer operations (consume, DLQ replay).
    /// </summary>
    public static readonly ActivitySource Consumer = new(EmitActivitySourceNames.Consumer, Version);

    /// <summary>
    /// Gets the ActivitySource for a specific provider.
    /// </summary>
    /// <param name="providerId">The provider identifier (e.g., "kafka").</param>
    /// <returns>The ActivitySource for the provider.</returns>
    public static ActivitySource ForProvider(string providerId)
    {
        return ProviderSources.GetOrAdd(
            providerId,
            id => new ActivitySource(EmitActivitySourceNames.ForProvider(id), Version));
    }
}

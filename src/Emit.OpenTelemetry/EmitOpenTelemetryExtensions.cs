namespace Emit.OpenTelemetry;

using Emit.Abstractions.Tracing;
using global::OpenTelemetry.Trace;

/// <summary>
/// Extension methods for integrating Emit with OpenTelemetry.
/// </summary>
public static class EmitOpenTelemetryExtensions
{
    /// <summary>
    /// Adds Emit ActivitySources to the OpenTelemetry tracer provider.
    /// </summary>
    /// <param name="builder">The tracer provider builder.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// This extension adds the following ActivitySources:
    /// <list type="bullet">
    /// <item><description><see cref="EmitActivitySourceNames.Outbox"/> - Outbox enqueue and processing operations</description></item>
    /// <item><description><see cref="EmitActivitySourceNames.Consumer"/> - Consumer operations</description></item>
    /// <item><description><see cref="EmitActivitySourceNames.ProviderPrefix"/>* - All provider operations (wildcard)</description></item>
    /// </list>
    /// </remarks>
    public static TracerProviderBuilder AddEmitInstrumentation(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .AddSource(EmitActivitySourceNames.Outbox)
            .AddSource(EmitActivitySourceNames.Consumer)
            .AddSource($"{EmitActivitySourceNames.ProviderPrefix}*"); // Wildcard for all providers
    }
}

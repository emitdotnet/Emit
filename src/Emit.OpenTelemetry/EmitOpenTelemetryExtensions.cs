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
    /// <item><description><see cref="EmitActivitySourceNames.Producer"/>: creating messages and sending them to the message broker</description></item>
    /// <item><description><see cref="EmitActivitySourceNames.Consumer"/>: processing messages, their retries and dead letters</description></item>
    /// <item><description><see cref="EmitActivitySourceNames.Mediator"/>: mediator requests</description></item>
    /// </list>
    /// </remarks>
    public static TracerProviderBuilder AddEmitInstrumentation(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .AddSource(EmitActivitySourceNames.Producer)
            .AddSource(EmitActivitySourceNames.Consumer)
            .AddSource(EmitActivitySourceNames.Mediator);
    }
}

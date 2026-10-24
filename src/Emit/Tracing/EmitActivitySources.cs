namespace Emit.Tracing;

using System.Diagnostics;
using Emit.Abstractions.Tracing;

/// <summary>
/// Provides singleton ActivitySource instances for Emit operations.
/// </summary>
internal static class EmitActivitySources
{
    /// <summary>
    /// The version of the telemetry the sources emit.
    /// </summary>
    internal const string Version = "1.0.0";

    /// <summary>
    /// Gets the ActivitySource for producing messages: create and send.
    /// </summary>
    public static readonly ActivitySource Producer = new(EmitActivitySourceNames.Producer, Version);

    /// <summary>
    /// Gets the ActivitySource for consuming messages: process, retry, and dead letter send.
    /// </summary>
    public static readonly ActivitySource Consumer = new(EmitActivitySourceNames.Consumer, Version);
}

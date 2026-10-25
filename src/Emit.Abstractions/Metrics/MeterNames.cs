namespace Emit.Abstractions.Metrics;

/// <summary>
/// The names of Emit's own meters. Use these when subscribing to Emit meters with OpenTelemetry or
/// <see cref="System.Diagnostics.Metrics.MeterListener"/>; each optional Emit package, such as a messaging provider or
/// the mediator, names its own meters and registers them as a <see cref="PackageMeter"/>.
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
    /// Daemon metrics: runs on each node, starts and failures.
    /// </summary>
    public const string EmitDaemon = "Emit.Daemon";
}

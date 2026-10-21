namespace Emit.Metrics;

using System.Diagnostics.Metrics;
using Emit.Abstractions.Metrics;

/// <summary>
/// Instruments for the <c>Emit.Daemon</c> meter: the daemon runs on a node, their starts and their failures.
/// </summary>
internal sealed class DaemonMetrics
{
    private readonly EmitMetricsEnrichment enrichment;

    // Set once by the node's coordinator; read by the meter on the exporter's thread
    private volatile Func<IReadOnlyList<DaemonRunCount>>? runCounts;

    /// <summary>
    /// Initializes a new instance of the <see cref="DaemonMetrics"/> class.
    /// </summary>
    /// <param name="meterFactory">Optional meter factory for managed meter lifetime. Falls back to an unmanaged meter when <c>null</c>.</param>
    /// <param name="enrichment">Static enrichment tags appended to all recordings.</param>
    public DaemonMetrics(IMeterFactory? meterFactory, EmitMetricsEnrichment enrichment)
    {
        ArgumentNullException.ThrowIfNull(enrichment);
        this.enrichment = enrichment;

        var meter = meterFactory?.Create(MeterNames.EmitDaemon) ?? new Meter(MeterNames.EmitDaemon);

        Starts = meter.CreateCounter<long>(
            "emit.daemon.starts", "{start}", "Count of daemon runs started on this node.");

        Failures = meter.CreateCounter<long>(
            "emit.daemon.failures", "{failure}", "Count of daemon runs that ended or threw without being asked to stop.");

        meter.CreateObservableGauge(
            "emit.daemon.runs",
            ObserveRuns,
            "{run}",
            "Daemon runs on this node: running, or stopping once asked to stop. Every daemon the node registers is reported, with zero when it has no run.");
    }

    internal Counter<long> Starts { get; }

    internal Counter<long> Failures { get; }

    /// <summary>
    /// Sets where the runs gauge reads the daemons of this node and their runs.
    /// </summary>
    internal void ObserveRunsWith(Func<IReadOnlyList<DaemonRunCount>> source) => runCounts = source;

    internal void RecordStart(string daemonId)
    {
        var tags = enrichment.CreateTags([new("daemon", daemonId)]);
        Starts.Add(1, tags);
    }

    internal void RecordFailure(string daemonId)
    {
        var tags = enrichment.CreateTags([new("daemon", daemonId)]);
        Failures.Add(1, tags);
    }

    private IEnumerable<Measurement<int>> ObserveRuns()
    {
        if (runCounts is not { } source)
        {
            yield break;
        }

        foreach (var count in source())
        {
            yield return new Measurement<int>(count.Running, enrichment.CreateTags([new("daemon", count.DaemonId), new("state", "running")]));
            yield return new Measurement<int>(count.Stopping, enrichment.CreateTags([new("daemon", count.DaemonId), new("state", "stopping")]));
        }
    }
}

/// <summary>
/// How many runs of a daemon a node has, by state.
/// </summary>
/// <param name="DaemonId">The daemon.</param>
/// <param name="Running">Runs that are running and hold their assignment.</param>
/// <param name="Stopping">Runs asked to stop, or whose node's lease ran out, that have not ended yet.</param>
internal readonly record struct DaemonRunCount(string DaemonId, int Running, int Stopping);

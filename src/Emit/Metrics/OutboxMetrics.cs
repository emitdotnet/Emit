namespace Emit.Metrics;

using System.Diagnostics.Metrics;
using Emit.Abstractions.Metrics;

/// <summary>
/// Instruments for the <c>Emit.Outbox</c> meter — outbox entry lifecycle and worker health metrics.
/// </summary>
public sealed class OutboxMetrics
{
    private readonly EmitMetricsEnrichment enrichment;

    private Func<int>? activeGroupsCallback;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxMetrics"/> class.
    /// </summary>
    /// <param name="meterFactory">Optional meter factory for managed meter lifetime. Falls back to an unmanaged meter when <c>null</c>.</param>
    /// <param name="enrichment">Static enrichment tags appended to all recordings.</param>
    public OutboxMetrics(IMeterFactory? meterFactory, EmitMetricsEnrichment enrichment)
    {
        ArgumentNullException.ThrowIfNull(enrichment);
        this.enrichment = enrichment;

        var meter = meterFactory?.Create(MeterNames.EmitOutbox) ?? new Meter(MeterNames.EmitOutbox);

        Enqueued = meter.CreateCounter<long>(
            "emit.outbox.enqueued", "{entry}", "Count of entries enqueued into the outbox.");

        ProcessingDuration = meter.CreateHistogram<double>(
            "emit.outbox.processing.duration", "s", "Duration of outbox entry processing.");

        ProcessingCompleted = meter.CreateCounter<long>(
            "emit.outbox.processing.completed", "{entry}", "Count of entries that completed processing.");

        CriticalTime = meter.CreateHistogram<double>(
            "emit.outbox.critical_time", "s", "End-to-end latency from enqueue to processed.");

        PollCycles = meter.CreateCounter<long>(
            "emit.outbox.worker.poll_cycles", "{cycle}", "Count of worker poll cycles.");

        BatchEntries = meter.CreateHistogram<int>(
            "emit.outbox.worker.batch_entries", "{entry}", "Number of entries fetched per poll cycle.");

        meter.CreateObservableGauge(
            "emit.outbox.worker.active_groups", ObserveActiveGroups, "{group}",
            "Number of groups being processed concurrently.");

        WorkerErrors = meter.CreateCounter<long>(
            "emit.outbox.worker.errors", "{error}", "Exceptions caught in the processing loop.");
    }

    internal Counter<long> Enqueued { get; }

    internal Histogram<double> ProcessingDuration { get; }

    internal Counter<long> ProcessingCompleted { get; }

    internal Histogram<double> CriticalTime { get; }

    internal Counter<long> PollCycles { get; }

    internal Histogram<int> BatchEntries { get; }

    internal Counter<long> WorkerErrors { get; }

    internal void RecordEnqueued(string system)
    {
        var tags = enrichment.CreateTags([new("system", system)]);
        Enqueued.Add(1, tags);
    }

    internal void RecordProcessingDuration(double seconds, string system, string result)
    {
        var tags = enrichment.CreateTags([new("system", system), new("result", result)]);
        ProcessingDuration.Record(seconds, tags);
    }

    internal void RecordProcessingCompleted(string system, string result)
    {
        var tags = enrichment.CreateTags([new("system", system), new("result", result)]);
        ProcessingCompleted.Add(1, tags);
    }

    internal void RecordCriticalTime(double seconds, string system)
    {
        var tags = enrichment.CreateTags([new("system", system)]);
        CriticalTime.Record(seconds, tags);
    }

    internal void RecordPollCycle(bool hasEntries)
    {
        var tags = enrichment.CreateTags([new("has_entries", hasEntries ? "true" : "false")]);
        PollCycles.Add(1, tags);
    }

    internal void RecordBatchEntries(int count)
    {
        var tags = enrichment.CreateTags();
        BatchEntries.Record(count, tags);
    }

    internal void RecordWorkerError()
    {
        var tags = enrichment.CreateTags();
        WorkerErrors.Add(1, tags);
    }

    internal void RegisterActiveGroupsCallback(Func<int> callback)
    {
        Volatile.Write(ref activeGroupsCallback, callback);
    }

    private Measurement<int> ObserveActiveGroups()
    {
        var value = Volatile.Read(ref activeGroupsCallback)?.Invoke() ?? 0;
        var tags = enrichment.CreateTags();
        return new Measurement<int>(value, tags);
    }
}

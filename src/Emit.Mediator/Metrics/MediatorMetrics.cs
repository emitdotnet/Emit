namespace Emit.Mediator.Metrics;

using System.Diagnostics.Metrics;
using Emit.Abstractions.Metrics;

/// <summary>
/// Instruments for the <c>Emit.Mediator</c> meter — mediator request dispatch metrics.
/// </summary>
public sealed class MediatorMetrics
{
    private readonly EmitMetricsEnrichment enrichment;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediatorMetrics"/> class.
    /// </summary>
    /// <param name="meterFactory">Optional meter factory for managed meter lifetime. Falls back to an unmanaged meter when <c>null</c>.</param>
    /// <param name="enrichment">Static enrichment tags appended to all recordings.</param>
    public MediatorMetrics(IMeterFactory? meterFactory, EmitMetricsEnrichment enrichment)
    {
        ArgumentNullException.ThrowIfNull(enrichment);
        this.enrichment = enrichment;

        var meter = meterFactory?.Create(MeterNames.EmitMediator) ?? new Meter(MeterNames.EmitMediator);

        SendDuration = meter.CreateHistogram<double>(
            "emit.mediator.send.duration", "s", "Full mediator pipeline execution time.");

        SendCompleted = meter.CreateCounter<long>(
            "emit.mediator.send.completed", "{request}", "Count of completed mediator pipeline executions.");

        SendActive = meter.CreateUpDownCounter<long>(
            "emit.mediator.send.active", "{request}", "Number of mediator requests currently being processed.");
    }

    internal Histogram<double> SendDuration { get; }

    internal Counter<long> SendCompleted { get; }

    internal UpDownCounter<long> SendActive { get; }

    internal void RecordSendDuration(double seconds, string requestType, string result)
    {
        var tags = enrichment.CreateTags([new("request_type", requestType), new("result", result)]);
        SendDuration.Record(seconds, tags);
    }

    internal void RecordSendCompleted(string requestType, string result)
    {
        var tags = enrichment.CreateTags([new("request_type", requestType), new("result", result)]);
        SendCompleted.Add(1, tags);
    }

    internal void RecordSendActiveIncrement()
    {
        var tags = enrichment.CreateTags();
        SendActive.Add(1, tags);
    }

    internal void RecordSendActiveDecrement()
    {
        var tags = enrichment.CreateTags();
        SendActive.Add(-1, tags);
    }
}

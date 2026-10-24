namespace Emit.Abstractions.Metrics;

using System.Diagnostics.Metrics;

/// <summary>
/// Instruments for the <c>Emit.Lock</c> meter — distributed locking metrics.
/// </summary>
public sealed class LockMetrics
{
    /// <summary>The <c>result</c> of a lock acquired.</summary>
    internal const string AcquireResultAcquired = "acquired";

    /// <summary>The <c>result</c> of a lock not acquired before the timeout.</summary>
    internal const string AcquireResultTimeout = "timeout";

    /// <summary>The <c>result</c> of a lock acquisition cancelled.</summary>
    internal const string AcquireResultCancelled = "cancelled";

    /// <summary>The <c>result</c> of a lock renewal that extended the lock.</summary>
    internal const string RenewalResultExtended = "extended";

    /// <summary>The <c>result</c> of a lock renewal that found the lock no longer held.</summary>
    internal const string RenewalResultLost = "lost";

    /// <summary>The <c>result</c> of a lock renewal that threw.</summary>
    internal const string RenewalResultFailed = "failed";

    /// <summary>The <c>result</c> of a lock renewal cancelled.</summary>
    internal const string RenewalResultCancelled = "cancelled";

    private readonly EmitMetricsEnrichment enrichment;

    /// <summary>
    /// Initializes a new instance of the <see cref="LockMetrics"/> class.
    /// </summary>
    /// <param name="meterFactory">Optional meter factory for managed meter lifetime. Falls back to an unmanaged meter when <c>null</c>.</param>
    /// <param name="enrichment">Static enrichment tags appended to all recordings.</param>
    public LockMetrics(IMeterFactory? meterFactory, EmitMetricsEnrichment enrichment)
    {
        ArgumentNullException.ThrowIfNull(enrichment);
        this.enrichment = enrichment;

        var meter = meterFactory?.Create(MeterNames.EmitLock) ?? new Meter(MeterNames.EmitLock);

        AcquireDuration = meter.CreateHistogram<double>(
            "emit.lock.acquire.duration", "s", "Duration of lock acquisition attempts.", tags: null, advice: HistogramAdvice.Seconds);

        AcquireRetries = meter.CreateHistogram<int>(
            "emit.lock.acquire.retries", "{attempt}", "Retry count for lock acquisition.");

        RenewalCompleted = meter.CreateCounter<long>(
            "emit.lock.renewal.completed", "{renewal}", "Count of lock renewal attempts.");

        HeldDuration = meter.CreateHistogram<double>(
            "emit.lock.held.duration", "s", "Duration locks are held.", tags: null, advice: HistogramAdvice.Seconds);
    }

    internal Histogram<double> AcquireDuration { get; }

    internal Histogram<int> AcquireRetries { get; }

    internal Counter<long> RenewalCompleted { get; }

    internal Histogram<double> HeldDuration { get; }

    internal void RecordAcquireDuration(double seconds, string result)
    {
        var tags = enrichment.CreateTags([new("result", result)]);
        AcquireDuration.Record(seconds, tags);
    }

    internal void RecordAcquireRetries(int attempts)
    {
        var tags = enrichment.CreateTags();
        AcquireRetries.Record(attempts, tags);
    }

    internal void RecordRenewalCompleted(string result)
    {
        var tags = enrichment.CreateTags([new("result", result)]);
        RenewalCompleted.Add(1, tags);
    }

    internal void RecordHeldDuration(double seconds)
    {
        var tags = enrichment.CreateTags();
        HeldDuration.Record(seconds, tags);
    }
}

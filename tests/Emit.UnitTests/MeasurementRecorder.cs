namespace Emit.UnitTests;

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using global::Emit.Abstractions.Metrics;

/// <summary>
/// Records the counter measurements of one meter made through <see cref="Enrichment"/>. Its tag is unique to this
/// recorder, which keeps out the measurements other tests make on the same meter at the same time.
/// </summary>
internal sealed class MeasurementRecorder : IDisposable
{
    private const string RecorderTag = "test.recorder";

    private readonly string id = Guid.NewGuid().ToString("N");
    private readonly MeterListener listener = new();
    private readonly ConcurrentQueue<(string Instrument, long Value, KeyValuePair<string, object?>[] Tags)> measurements = new();

    public MeasurementRecorder(string meterName)
    {
        Enrichment = new EmitMetricsEnrichment(new KeyValuePair<string, object?>[] { new(RecorderTag, id) });
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == meterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var all = tags.ToArray();
            if (all.Any(t => t.Key == RecorderTag && Equals(t.Value, id)))
                measurements.Enqueue((instrument.Name, value, all));
        });
        listener.Start();
    }

    /// <summary>
    /// Gets the enrichment to build the metrics under test with.
    /// </summary>
    public EmitMetricsEnrichment Enrichment { get; }

    /// <summary>
    /// Returns the total recorded on <paramref name="instrument"/> with the tag <paramref name="tag"/> set to
    /// <paramref name="value"/>.
    /// </summary>
    public long Total(string instrument, string tag, string value) =>
        measurements
            .Where(m => m.Instrument == instrument && m.Tags.Any(t => t.Key == tag && Equals(t.Value, value)))
            .Sum(m => m.Value);

    /// <summary>
    /// Returns the total recorded on <paramref name="instrument"/>.
    /// </summary>
    public long Total(string instrument) =>
        measurements.Where(m => m.Instrument == instrument).Sum(m => m.Value);

    public void Dispose() => listener.Dispose();
}

namespace Emit.Abstractions.Metrics;

using System.Diagnostics.Metrics;

/// <summary>
/// The bucket boundaries Emit's histograms advise, so that any metrics pipeline aggregates them sensibly without
/// configuring views.
/// </summary>
public static class HistogramAdvice
{
    /// <summary>
    /// Boundaries for durations in seconds, from a millisecond to ten seconds.
    /// </summary>
    public static InstrumentAdvice<double> Seconds { get; } = new()
    {
        HistogramBucketBoundaries = [0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10],
    };

    /// <summary>
    /// Boundaries for sizes in bytes, from 256 bytes to a megabyte.
    /// </summary>
    public static InstrumentAdvice<int> Bytes { get; } = new()
    {
        HistogramBucketBoundaries = [256, 1_024, 4_096, 16_384, 65_536, 524_288, 1_048_576],
    };
}

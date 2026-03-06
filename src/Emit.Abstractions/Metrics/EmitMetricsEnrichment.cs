namespace Emit.Abstractions.Metrics;

using System.Diagnostics;

/// <summary>
/// Holds static enrichment tags that are appended to every metric recorded by Emit.
/// Registered as a singleton in dependency injection with empty defaults.
/// Configure tags via <c>AddEmitInstrumentation()</c> in the <c>Emit.OpenTelemetry</c> package.
/// </summary>
public sealed class EmitMetricsEnrichment
{
    /// <summary>
    /// Initializes a new instance with no enrichment tags.
    /// </summary>
    public EmitMetricsEnrichment()
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified enrichment tags.
    /// </summary>
    /// <param name="tags">Static tags to append to all metrics.</param>
    public EmitMetricsEnrichment(ReadOnlyMemory<KeyValuePair<string, object?>> tags)
    {
        Tags = tags;
    }

    /// <summary>
    /// Static tags appended to every metric recording.
    /// </summary>
    public ReadOnlyMemory<KeyValuePair<string, object?>> Tags { get; } = ReadOnlyMemory<KeyValuePair<string, object?>>.Empty;

    /// <summary>
    /// Creates a <see cref="TagList"/> that combines the specified metric-specific tags
    /// with the static enrichment tags.
    /// </summary>
    /// <param name="metricTags">Tags specific to the metric being recorded.</param>
    /// <returns>A <see cref="TagList"/> containing both enrichment and metric-specific tags.</returns>
    public TagList CreateTags(ReadOnlySpan<KeyValuePair<string, object?>> metricTags)
    {
        var tags = new TagList();

        foreach (var tag in Tags.Span)
        {
            tags.Add(tag);
        }

        foreach (var tag in metricTags)
        {
            tags.Add(tag);
        }

        return tags;
    }

    /// <summary>
    /// Creates a <see cref="TagList"/> containing only the static enrichment tags.
    /// </summary>
    /// <returns>A <see cref="TagList"/> containing the enrichment tags.</returns>
    public TagList CreateTags()
    {
        var tags = new TagList();

        foreach (var tag in Tags.Span)
        {
            tags.Add(tag);
        }

        return tags;
    }
}

namespace Emit.Tracing;

using System.Text;
using Emit.Abstractions;

/// <summary>
/// Injects W3C trace context (traceparent, tracestate, baggage) from
/// <see cref="IActivityFeature"/> into message headers.
/// </summary>
public static class TraceContextHeaderInjector
{
    /// <summary>
    /// Appends trace context headers as UTF-8 encoded byte arrays.
    /// </summary>
    /// <param name="activityFeature">The activity feature, or <c>null</c> if tracing is not active.</param>
    /// <param name="headers">The mutable header collection to append to.</param>
    public static void InjectByteHeaders(
        IActivityFeature? activityFeature,
        ICollection<KeyValuePair<string, byte[]>> headers)
    {
        if (activityFeature is null) return;

        if (activityFeature.TraceParent is not null)
            headers.Add(new("traceparent", Encoding.UTF8.GetBytes(activityFeature.TraceParent)));

        if (activityFeature.TraceState is not null)
            headers.Add(new("tracestate", Encoding.UTF8.GetBytes(activityFeature.TraceState)));

        foreach (var (key, value) in activityFeature.Baggage)
            headers.Add(new($"baggage-{key}", Encoding.UTF8.GetBytes(value)));
    }

    /// <summary>
    /// Appends trace context headers as strings.
    /// </summary>
    /// <param name="activityFeature">The activity feature, or <c>null</c> if tracing is not active.</param>
    /// <param name="headers">The mutable header collection to append to.</param>
    public static void InjectStringHeaders(
        IActivityFeature? activityFeature,
        ICollection<KeyValuePair<string, string>> headers)
    {
        if (activityFeature is null) return;

        if (activityFeature.TraceParent is not null)
            headers.Add(new("traceparent", activityFeature.TraceParent));

        if (activityFeature.TraceState is not null)
            headers.Add(new("tracestate", activityFeature.TraceState));

        foreach (var (key, value) in activityFeature.Baggage)
            headers.Add(new($"baggage-{key}", value));
    }
}

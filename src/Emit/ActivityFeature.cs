namespace Emit;

using System.Diagnostics;
using Emit.Abstractions;

/// <summary>
/// Captures distributed tracing context from the current Activity.
/// </summary>
internal sealed class ActivityFeature : IActivityFeature
{
    public string? TraceParent { get; }
    public string? TraceState { get; }
    public IEnumerable<KeyValuePair<string, string>> Baggage { get; }

    private ActivityFeature(Activity activity)
    {
        TraceParent = activity.Id;
        TraceState = string.IsNullOrEmpty(activity.TraceStateString) ? null : activity.TraceStateString;
        // Materialize baggage and filter out null values
        Baggage = activity.Baggage
            .Where(b => b.Value is not null)
            .Select(b => new KeyValuePair<string, string>(b.Key, b.Value!))
            .ToList();
    }

    /// <summary>
    /// Creates a feature from the current Activity, or returns <c>null</c> if no Activity is active.
    /// </summary>
    public static ActivityFeature? FromCurrent()
    {
        return Activity.Current is { } activity ? new ActivityFeature(activity) : null;
    }
}

namespace Emit.Tracing;

using System.Diagnostics;
using Microsoft.Extensions.Logging;

/// <summary>
/// Internal helper for Activity management operations.
/// </summary>
internal static class ActivityHelper
{
    /// <summary>
    /// Creates an Activity from a W3C traceparent string with parsed parent context.
    /// </summary>
    /// <param name="source">The ActivitySource to create the Activity from.</param>
    /// <param name="name">The Activity name.</param>
    /// <param name="kind">The ActivityKind.</param>
    /// <param name="traceParent">The W3C traceparent string.</param>
    /// <param name="traceState">The optional W3C tracestate string.</param>
    /// <param name="configureTags">Optional action to configure tags.</param>
    /// <returns>The created Activity, or <c>null</c> if the ActivitySource is not enabled.</returns>
    public static Activity? StartActivityFromTraceParent(
        ActivitySource source,
        string name,
        ActivityKind kind,
        string? traceParent,
        string? traceState,
        Action<Activity>? configureTags = null)
    {
        ActivityContext parentContext = default;
        if (!string.IsNullOrEmpty(traceParent))
        {
            ActivityContext.TryParse(traceParent, traceState, out parentContext);
        }

        var activity = source.StartActivity(name, kind, parentContext);
        configureTags?.Invoke(activity!);
        return activity;
    }

    /// <summary>
    /// Restores baggage from message headers to the specified Activity.
    /// </summary>
    /// <param name="activity">The Activity to restore baggage to.</param>
    /// <param name="headers">The message headers containing baggage.</param>
    public static void RestoreBaggage(Activity activity, IEnumerable<KeyValuePair<string, string>> headers)
    {
        foreach (var header in headers.Where(h => h.Key.StartsWith("baggage-", StringComparison.Ordinal)))
        {
            var key = header.Key[8..]; // Remove "baggage-" prefix
            activity.AddBaggage(key, header.Value);
        }
    }

    /// <summary>
    /// Validates and truncates baggage to the specified maximum size.
    /// </summary>
    /// <param name="baggage">The baggage items to validate.</param>
    /// <param name="maxSizeBytes">The maximum total size in bytes.</param>
    /// <param name="logger">The logger for warnings.</param>
    /// <returns>The validated and potentially truncated baggage items.</returns>
    public static IEnumerable<KeyValuePair<string, string>> ValidateAndTruncateBaggage(
        IEnumerable<KeyValuePair<string, string>> baggage,
        int maxSizeBytes,
        ILogger logger)
    {
        var baggageList = baggage.ToList();
        if (baggageList.Count == 0)
        {
            return baggageList;
        }

        var totalSize = baggageList.Sum(b => b.Key.Length + b.Value.Length);

        if (totalSize <= maxSizeBytes)
        {
            return baggageList;
        }

        // Truncate baggage to max size
        logger.LogWarning(
            "Baggage size {Size} bytes exceeds limit {Limit} bytes. Truncating to fit.",
            totalSize, maxSizeBytes);

        var kept = new List<KeyValuePair<string, string>>();
        var currentSize = 0;
        foreach (var item in baggageList)
        {
            var itemSize = item.Key.Length + item.Value.Length;
            if (currentSize + itemSize <= maxSizeBytes)
            {
                kept.Add(item);
                currentSize += itemSize;
            }
            else
            {
                break; // Drop remaining items
            }
        }

        return kept;
    }
}

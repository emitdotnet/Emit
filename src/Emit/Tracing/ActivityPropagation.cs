namespace Emit.Tracing;

using System.Diagnostics;
using System.Net;
using Emit.Abstractions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Internal helper for Activity management operations.
/// </summary>
internal static class ActivityPropagation
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
    /// Restores baggage from a W3C baggage header value to the specified Activity.
    /// Parses the comma-separated <c>key=value</c> format per the W3C Baggage specification.
    /// </summary>
    /// <param name="activity">The Activity to restore baggage to.</param>
    /// <param name="baggageHeaderValue">The W3C baggage header value.</param>
    public static void RestoreBaggage(Activity activity, string baggageHeaderValue)
    {
        foreach (var member in baggageHeaderValue.Split(','))
        {
            var trimmed = member.Trim();
            if (trimmed.Length == 0)
                continue;

            var equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex <= 0)
                continue;

            // Strip any properties (;property=value) — take only key=value
            var valueEnd = trimmed.IndexOf(';', equalsIndex);
            var key = WebUtility.UrlDecode(trimmed[..equalsIndex].Trim());
            var value = WebUtility.UrlDecode(
                valueEnd > 0
                    ? trimmed[(equalsIndex + 1)..valueEnd].Trim()
                    : trimmed[(equalsIndex + 1)..].Trim());

            activity.AddBaggage(key, value);
        }
    }

    /// <summary>
    /// Builds a W3C baggage header value from Activity baggage items, validating
    /// total size does not exceed the configured limit.
    /// </summary>
    /// <param name="baggage">The baggage items to serialize.</param>
    /// <param name="maxSizeBytes">The maximum total size in bytes.</param>
    /// <param name="logger">The logger for warnings.</param>
    /// <returns>The W3C baggage header value, or <c>null</c> if no baggage.</returns>
    public static string? BuildBaggageHeader(
        IEnumerable<KeyValuePair<string, string>> baggage,
        int maxSizeBytes,
        ILogger logger)
    {
        var members = new List<string>();
        var totalSize = 0;

        foreach (var (key, value) in baggage)
        {
            var encodedKey = WebUtility.UrlEncode(key);
            var encodedValue = WebUtility.UrlEncode(value);
            var member = $"{encodedKey}={encodedValue}";

            // +2 for ", " separator (except first)
            var additionalSize = member.Length + (members.Count > 0 ? 2 : 0);

            if (totalSize + additionalSize > maxSizeBytes)
            {
                logger.LogWarning(
                    "Baggage size exceeds limit {Limit} bytes. Dropping remaining items.",
                    maxSizeBytes);
                break;
            }

            members.Add(member);
            totalSize += additionalSize;
        }

        return members.Count > 0 ? string.Join(", ", members) : null;
    }
}

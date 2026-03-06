namespace Emit.Abstractions;
/// <summary>
/// Extension methods for <see cref="IMessageSourceFeature"/>.
/// </summary>
public static class MessageSourceFeatureExtensions
{
    /// <summary>
    /// Formats the source properties into a compact string for log messages.
    /// Returns <c>"unknown"</c> when the feature is <c>null</c> or has no properties.
    /// </summary>
    /// <param name="feature">The message source feature, or <c>null</c>.</param>
    /// <returns>A formatted string such as <c>"topic=orders, partition=2, offset=1042"</c>.</returns>
    public static string FormatSource(this IMessageSourceFeature? feature)
    {
        if (feature is null || feature.Properties.Count == 0)
        {
            return "unknown";
        }

        return string.Join(", ", feature.Properties.Select(p => $"{p.Key}={p.Value}"));
    }
}

namespace Emit.Tracing;

using System.Diagnostics;
using System.Text;
using Emit.Abstractions;
using Emit.Models;

/// <summary>
/// Provides helpers for creating and managing distributed tracing activities during outbox processing.
/// </summary>
public static class OutboxActivityHelper
{
    /// <summary>
    /// Restores an <see cref="ActivityContext"/> from an <see cref="OutboxEntry"/>'s headers.
    /// </summary>
    /// <param name="entry">The outbox entry containing W3C trace context in its headers.</param>
    /// <returns>
    /// The restored <see cref="ActivityContext"/> if the entry headers contain a valid traceparent;
    /// otherwise, <see langword="default"/>.
    /// </returns>
    public static ActivityContext RestoreContext(OutboxEntry entry)
    {
        string? traceParent = null;
        string? traceState = null;

        foreach (var header in entry.Headers)
        {
            if (header.Key == WellKnownHeaders.TraceParent)
                traceParent = Encoding.UTF8.GetString(header.Value);
            else if (header.Key == WellKnownHeaders.TraceState)
                traceState = Encoding.UTF8.GetString(header.Value);
        }

        if (!string.IsNullOrEmpty(traceParent) &&
            ActivityContext.TryParse(traceParent, traceState, out var context))
        {
            return context;
        }

        return default;
    }

    /// <summary>
    /// Starts an <see cref="Activity"/> for outbox processing, restoring the parent context from the entry.
    /// </summary>
    /// <param name="activitySource">The <see cref="ActivitySource"/> used to create the activity.</param>
    /// <param name="entry">The outbox entry whose trace context is restored as the parent.</param>
    /// <param name="name">The activity name. Defaults to <c>emit.outbox.process</c>.</param>
    /// <returns>
    /// The started <see cref="Activity"/>, or <see langword="null"/> if the source is not enabled
    /// or no listener is active.
    /// </returns>
    public static Activity? StartProcessActivity(ActivitySource activitySource, OutboxEntry entry, string name = "emit.outbox.process")
    {
        var parentContext = RestoreContext(entry);
        var activity = activitySource.StartActivity(name, ActivityKind.Internal, parentContext);

        if (activity is not null)
        {
            activity.SetTag("emit.sequence", entry.Sequence);
            activity.SetTag("emit.group.key", entry.GroupKey);

            activity.SetTag("messaging.destination.name", entry.Destination);

            if (entry.Properties.TryGetValue("valueType", out var valueType))
            {
                activity.SetTag("emit.message.type", valueType);
            }

            if (entry.Properties.TryGetValue("keyType", out var keyType))
            {
                activity.SetTag("emit.key.type", keyType);
            }
        }

        return activity;
    }
}

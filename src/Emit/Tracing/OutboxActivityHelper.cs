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
    /// <param name="nodeId">The node identifier to tag on the activity.</param>
    /// <param name="name">The activity name. Defaults to <see cref="ActivityNames.OutboxProcess"/>.</param>
    /// <returns>
    /// The started <see cref="Activity"/>, or <see langword="null"/> if the source is not enabled
    /// or no listener is active.
    /// </returns>
    public static Activity? StartProcessActivity(ActivitySource activitySource, OutboxEntry entry, Guid nodeId, string name = ActivityNames.OutboxProcess)
    {
        var parentContext = RestoreContext(entry);
        var activity = activitySource.StartActivity(name, ActivityKind.Internal, parentContext);

        if (activity is not null)
        {
            activity.SetTag(ActivityTagNames.NodeId, nodeId.ToString());
            activity.SetTag(ActivityTagNames.Sequence, entry.Sequence);
            activity.SetTag(ActivityTagNames.GroupKey, entry.GroupKey);

            activity.SetTag(ActivityTagNames.MessagingDestinationName, entry.Destination);

            if (entry.Properties.TryGetValue("valueType", out var valueType))
            {
                activity.SetTag(ActivityTagNames.MessageType, valueType);
            }

            if (entry.Properties.TryGetValue("keyType", out var keyType))
            {
                activity.SetTag(ActivityTagNames.KeyType, keyType);
            }
        }

        return activity;
    }
}

namespace Emit.Tracing;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Microsoft.Extensions.Options;

/// <summary>
/// Transport-level middleware that creates an <c>emit.receive</c> parent Activity.
/// Extracts traceparent from <see cref="TransportContext.Headers"/> for distributed
/// trace propagation. Fan-out produces N child <c>emit.consume</c> activities — one per
/// consumer entry.
/// </summary>
internal sealed class TransportTracingMiddleware(
    IOptions<EmitTracingOptions> options,
    INodeIdentity nodeIdentity) : IMiddleware<TransportContext>
{
    private readonly EmitTracingOptions options = options.Value;

    /// <inheritdoc />
    public async Task InvokeAsync(TransportContext context, IMiddlewarePipeline<TransportContext> next)
    {
        if (!this.options.Enabled)
        {
            await next.InvokeAsync(context).ConfigureAwait(false);
            return;
        }

        // Extract traceparent and tracestate from headers
        ActivityContext parentContext = default;

        if (context.Headers is { Count: > 0 })
        {
            var headerMap = context.Headers.ToDictionary(
                h => h.Key, h => h.Value, StringComparer.OrdinalIgnoreCase);

            headerMap.TryGetValue(WellKnownHeaders.TraceParent, out var traceParent);
            headerMap.TryGetValue(WellKnownHeaders.TraceState, out var traceState);

            if (!string.IsNullOrEmpty(traceParent))
            {
                ActivityContext.TryParse(traceParent, traceState, out parentContext);
            }
        }

        // Create parent "emit.receive" Activity
        using var activity = EmitActivitySources.Consumer.StartActivity(
            ActivityNames.Receive,
            ActivityKind.Consumer,
            parentContext);

        if (activity is not null)
        {
            activity.SetTag(ActivityTagNames.NodeId, nodeIdentity.NodeId.ToString());
            activity.SetTag(ActivityTagNames.MessagingSystem,
                EmitEndpointAddress.GetScheme(context.DestinationAddress) ?? "emit");
            activity.SetTag(ActivityTagNames.MessagingOperation, "receive");

            if (EmitEndpointAddress.GetEntityName(context.DestinationAddress) is { } destination)
            {
                activity.SetTag(ActivityTagNames.MessagingDestinationName, destination);
            }
        }

        await next.InvokeAsync(context).ConfigureAwait(false);
    }
}

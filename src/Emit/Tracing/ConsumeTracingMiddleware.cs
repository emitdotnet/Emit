namespace Emit.Tracing;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Abstractions.Tracing;
using Microsoft.Extensions.Options;

/// <summary>
/// Middleware that creates an <c>emit.consume</c> child Activity for each consumer entry.
/// Consumer identity is baked in at build time by <see cref="Pipeline.ConsumerPipelineComposer{TValue}"/>.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
internal sealed class ConsumeTracingMiddleware<TMessage>(
    IOptions<EmitTracingOptions> options,
    ActivityEnricherInvoker enricherInvoker,
    INodeIdentity nodeIdentity,
    string consumerIdentifier,
    Type? consumerType) : IMiddleware<ConsumeContext<TMessage>>
{
    private readonly EmitTracingOptions options = options.Value;

    public async Task InvokeAsync(
        ConsumeContext<TMessage> context,
        IMiddlewarePipeline<ConsumeContext<TMessage>> next)
    {
        // Check if tracing is enabled
        if (!this.options.Enabled)
        {
            await next.InvokeAsync(context).ConfigureAwait(false);
            return;
        }

        // Extract traceparent and tracestate from headers (direct property on ConsumeContext)
        var headers = context.Headers;
        ActivityContext parentContext = default;
        string? traceParent = null;
        string? traceState = null;
        bool isDlqReplay = false;

        if (headers is { Count: > 0 })
        {
            // Check if this is a DLQ replay (has emit.dlq.original_traceparent)
            var originalTraceParent = headers.FirstOrDefault(h => h.Key == DeadLetterHeaders.OriginalTraceParent).Value;
            if (!string.IsNullOrEmpty(originalTraceParent))
            {
                // This is a DLQ replay — link back to the original failed trace
                traceParent = originalTraceParent;
                isDlqReplay = true;
            }
            else
            {
                // Normal consume — use standard traceparent
                traceParent = headers.FirstOrDefault(h => h.Key == WellKnownHeaders.TraceParent).Value;
            }

            traceState = headers.FirstOrDefault(h => h.Key == WellKnownHeaders.TraceState).Value;

            if (!string.IsNullOrEmpty(traceParent))
            {
                ActivityContext.TryParse(traceParent, traceState, out parentContext);
            }
        }

        // Create Activity for consume operation (or DLQ replay)
        using var activity = EmitActivitySources.Consumer.StartActivity(
            isDlqReplay ? ActivityNames.DlqReplay : ActivityNames.Consume,
            ActivityKind.Consumer,
            parentContext);

        if (activity is null)
        {
            // ActivitySource not enabled (no listeners)
            await next.InvokeAsync(context).ConfigureAwait(false);
            return;
        }

        activity.SetTag(ActivityTagNames.NodeId, nodeIdentity.NodeId.ToString());

        // Add standard tags — derive messaging.system from URI scheme
        activity.SetTag(ActivityTagNames.MessagingSystem,
            EmitEndpointAddress.GetScheme(context.DestinationAddress) ?? "emit");
        activity.SetTag(ActivityTagNames.MessagingOperation, "receive");
        activity.SetTag(ActivityTagNames.MessageType, typeof(TMessage).Name);

        // Add consumer identity (baked at build time)
        activity.SetTag(ActivityTagNames.Consumer, consumerIdentifier);
        if (consumerType is not null)
        {
            activity.SetTag(ActivityTagNames.ConsumerType, consumerType.Name);
        }

        // Add destination name from URI
        if (EmitEndpointAddress.GetEntityName(context.DestinationAddress) is { } destination)
        {
            activity.SetTag(ActivityTagNames.MessagingDestinationName, destination);
        }

        // Add DLQ replay indicator if applicable
        if (isDlqReplay)
        {
            activity.SetTag(ActivityTagNames.DlqReplay, true);

            // Extract original topic from headers if available
            var originalTopic = headers.FirstOrDefault(h => h.Key == DeadLetterHeaders.OriginalTopic).Value;
            if (!string.IsNullOrEmpty(originalTopic))
            {
                activity.SetTag(ActivityTagNames.DlqOriginalTopic, originalTopic);
            }
        }

        // Restore baggage from W3C baggage header
        if (this.options.PropagateBaggage && headers is { Count: > 0 })
        {
            var baggageHeader = headers.FirstOrDefault(h => h.Key == WellKnownHeaders.Baggage).Value;
            if (!string.IsNullOrEmpty(baggageHeader))
            {
                ActivityPropagation.RestoreBaggage(activity, baggageHeader);
            }
        }

        // Invoke enrichers
        enricherInvoker.InvokeEnrichers(
            activity,
            new EnrichmentContext
            {
                MessageContext = context,
                Phase = "consume"
            },
            context.Services);

        // Continue pipeline
        await next.InvokeAsync(context).ConfigureAwait(false);
    }
}

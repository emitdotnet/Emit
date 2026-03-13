namespace Emit.Tracing;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Abstractions.Tracing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Middleware that creates a distributed tracing Activity for outbound messages and injects
/// W3C trace context (traceparent, tracestate, baggage) into <see cref="SendContext{T}.Headers"/>.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
internal sealed class ProduceTracingMiddleware<TMessage>(
    IOptions<EmitTracingOptions> options,
    ActivityEnricherInvoker enricherInvoker,
    INodeIdentity nodeIdentity,
    ILogger<ProduceTracingMiddleware<TMessage>> logger) : IMiddleware<SendContext<TMessage>>
{
    private readonly EmitTracingOptions options = options.Value;

    public async Task InvokeAsync(
        SendContext<TMessage> context,
        IMiddlewarePipeline<SendContext<TMessage>> next)
    {
        // Check if tracing is enabled
        if (!this.options.Enabled)
        {
            await next.InvokeAsync(context).ConfigureAwait(false);
            return;
        }

        // Check if we should create an Activity
        var shouldCreateActivity = Activity.Current is not null || this.options.CreateRootActivities;
        if (!shouldCreateActivity)
        {
            await next.InvokeAsync(context).ConfigureAwait(false);
            return;
        }

        // Create Activity for outbound operation
        using var activity = EmitActivitySources.Outbox.StartActivity(
            ActivityNames.Produce,
            ActivityKind.Producer);

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
        activity.SetTag(ActivityTagNames.MessagingOperation, "publish");
        activity.SetTag(ActivityTagNames.MessageType, typeof(TMessage).Name);

        if (EmitEndpointAddress.GetEntityName(context.DestinationAddress) is { } destination)
        {
            activity.SetTag(ActivityTagNames.MessagingDestinationName, destination);
        }

        // Inject W3C trace context + source address into context.Headers
        InjectTraceContext(activity, context.Headers, context.SourceAddress);

        // Invoke enrichers
        enricherInvoker.InvokeEnrichers(
            activity,
            new EnrichmentContext
            {
                MessageContext = context,
                Phase = "produce"
            },
            context.Services);

        // Continue pipeline
        await next.InvokeAsync(context).ConfigureAwait(false);
    }

    private void InjectTraceContext(
        Activity activity,
        List<KeyValuePair<string, string>> headers,
        Uri? sourceAddress)
    {
        var traceParent = activity.Id;
        if (traceParent is not null)
        {
            headers.Add(new(WellKnownHeaders.TraceParent, traceParent));
        }

        var traceState = activity.TraceStateString;
        if (traceState is not null)
        {
            headers.Add(new(WellKnownHeaders.TraceState, traceState));
        }

        // Inject source address so consumers can reconstruct SourceAddress
        if (sourceAddress is not null)
        {
            headers.Add(new(WellKnownHeaders.SourceAddress, sourceAddress.ToString()));
        }

        if (this.options.PropagateBaggage)
        {
            // Activity.Baggage has nullable values — filter to non-null
            var baggage = activity.Baggage
                .Where(b => b.Value is not null)
                .Select(b => new KeyValuePair<string, string>(b.Key, b.Value!));

            var baggageHeader = ActivityHelper.BuildBaggageHeader(
                baggage,
                this.options.MaxBaggageSizeBytes,
                logger);

            if (baggageHeader is not null)
            {
                headers.Add(new(WellKnownHeaders.Baggage, baggageHeader));
            }
        }
    }
}

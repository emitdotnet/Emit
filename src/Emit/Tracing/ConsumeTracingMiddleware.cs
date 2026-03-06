namespace Emit.Tracing;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Abstractions.Tracing;
using Microsoft.Extensions.Options;

/// <summary>
/// Middleware that restores distributed tracing context from message headers for inbound messages.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
internal sealed class ConsumeTracingMiddleware<TMessage>(
    IOptions<EmitTracingOptions> options,
    ActivityEnricherInvoker enricherInvoker) : IMiddleware<InboundContext<TMessage>>
{
    private readonly EmitTracingOptions options = options.Value;

    public async Task InvokeAsync(
        InboundContext<TMessage> context,
        MessageDelegate<InboundContext<TMessage>> next)
    {
        // Check if tracing is enabled
        if (!this.options.Enabled)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Extract traceparent and tracestate from headers
        var headers = context.Features.Get<IHeadersFeature>()?.Headers;
        ActivityContext parentContext = default;
        string? traceParent = null;
        string? traceState = null;
        bool isDlqReplay = false;

        if (headers is not null)
        {
            // Check if this is a DLQ replay (has emit.dlq.original_traceparent)
            var originalTraceParent = headers.FirstOrDefault(h => h.Key == "emit.dlq.original_traceparent").Value;
            if (!string.IsNullOrEmpty(originalTraceParent))
            {
                // This is a DLQ replay — link back to the original failed trace
                traceParent = originalTraceParent;
                isDlqReplay = true;
            }
            else
            {
                // Normal consume — use standard traceparent
                traceParent = headers.FirstOrDefault(h => h.Key == "traceparent").Value;
            }

            traceState = headers.FirstOrDefault(h => h.Key == "tracestate").Value;

            if (!string.IsNullOrEmpty(traceParent))
            {
                ActivityContext.TryParse(traceParent, traceState, out parentContext);
            }
        }

        // Create Activity for consume operation (or DLQ replay)
        using var activity = EmitActivitySources.Consumer.StartActivity(
            isDlqReplay ? "emit.dlq.replay" : "emit.consume",
            ActivityKind.Consumer,
            parentContext);

        if (activity is null)
        {
            // ActivitySource not enabled (no listeners)
            await next(context).ConfigureAwait(false);
            return;
        }

        // Add standard tags
        activity.SetTag("messaging.system", "emit");
        activity.SetTag("messaging.operation", "receive");
        activity.SetTag("emit.message.type", typeof(TMessage).Name);

        // Add topic from source metadata
        if (context.Features.Get<IMessageSourceFeature>() is { } source &&
            source.Properties.TryGetValue("topic", out var topicName))
        {
            activity.SetTag("messaging.destination.name", topicName);
        }

        // Add key type
        if (context.Features.Get<IKeyTypeFeature>() is { } keyType)
        {
            activity.SetTag("emit.key.type", keyType.KeyType.Name);
        }

        // Add DLQ replay indicator if applicable
        if (isDlqReplay)
        {
            activity.SetTag("emit.dlq.replay", true);

            // Extract original topic from headers if available
            var originalTopic = headers?.FirstOrDefault(h => h.Key == "emit.dlq.original_topic").Value;
            if (!string.IsNullOrEmpty(originalTopic))
            {
                activity.SetTag("emit.dlq.original_topic", originalTopic);
            }
        }

        // Add provider ID if available
        if (context.Features.Get<IProviderIdentifierFeature>() is { } provider)
        {
            activity.SetTag("emit.provider.id", provider.ProviderId);
        }

        // Restore baggage from headers
        if (this.options.PropagateBaggage && headers is not null)
        {
            ActivityHelper.RestoreBaggage(activity, headers);
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
        await next(context).ConfigureAwait(false);

        // Tag consumer identity (set pre-pipeline by FanOutAsync, may be updated by router)
        if (context.Features.Get<IConsumerIdentityFeature>() is { } identity)
        {
            activity.SetTag("emit.consumer", identity.Identifier);

            if (identity.ConsumerType is not null)
            {
                activity.SetTag("emit.consumer.type", identity.ConsumerType.Name);
            }

            if (identity.RouteKey is not null)
            {
                activity.SetTag("emit.route.key", identity.RouteKey.ToString());
            }
        }
    }
}

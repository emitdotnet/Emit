namespace Emit.Tracing;

using System.Diagnostics;
using Emit;
using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Abstractions.Tracing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Middleware that captures distributed tracing context from the current Activity for outbound messages.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
internal sealed class ProduceTracingMiddleware<TMessage>(
    IOptions<EmitTracingOptions> options,
    ActivityEnricherInvoker enricherInvoker,
    ILogger<ProduceTracingMiddleware<TMessage>> logger) : IMiddleware<OutboundContext<TMessage>>
{
    private readonly EmitTracingOptions options = options.Value;

    public async Task InvokeAsync(
        OutboundContext<TMessage> context,
        MessageDelegate<OutboundContext<TMessage>> next)
    {
        // Check if tracing is enabled
        if (!this.options.Enabled)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Check if we should create an Activity
        var shouldCreateActivity = Activity.Current is not null || this.options.CreateRootActivities;
        if (!shouldCreateActivity)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Create Activity for outbound operation
        using var activity = EmitActivitySources.Outbox.StartActivity(
            "emit.produce",
            ActivityKind.Producer);

        if (activity is null)
        {
            // ActivitySource not enabled (no listeners)
            await next(context).ConfigureAwait(false);
            return;
        }

        // Add standard tags
        activity.SetTag("messaging.system", "emit");
        activity.SetTag("messaging.operation", "publish");
        activity.SetTag("emit.message.type", typeof(TMessage).Name);

        // Add provider ID if available
        if (context.Features.Get<IProviderIdentifierFeature>() is { } provider)
        {
            activity.SetTag("emit.provider.id", provider.ProviderId);
        }

        // Add topic from source metadata
        if (context.Features.Get<IMessageSourceFeature>() is { } source &&
            source.Properties.TryGetValue("topic", out var topic))
        {
            activity.SetTag("messaging.destination.name", topic);
        }

        // Add key type
        if (context.Features.Get<IKeyTypeFeature>() is { } keyType)
        {
            activity.SetTag("emit.key.type", keyType.KeyType.Name);
        }

        // Capture trace context and baggage
        var feature = ActivityFeature.FromCurrent();
        if (feature is not null)
        {
            // Validate and truncate baggage if needed
            if (this.options.PropagateBaggage && feature.Baggage.Any())
            {
                var validatedBaggage = ActivityHelper.ValidateAndTruncateBaggage(
                    feature.Baggage,
                    this.options.MaxBaggageSizeBytes,
                    logger);

                // Set validated baggage on the feature (requires creating new feature)
                context.Features.Set<IActivityFeature>(new BaggageValidatedActivityFeature(
                    feature.TraceParent,
                    feature.TraceState,
                    validatedBaggage));
            }
            else
            {
                context.Features.Set<IActivityFeature>(feature);
            }
        }

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
        await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Internal wrapper to hold validated baggage.
    /// </summary>
    private sealed class BaggageValidatedActivityFeature(
        string? traceParent,
        string? traceState,
        IEnumerable<KeyValuePair<string, string>> baggage) : IActivityFeature
    {
        public string? TraceParent { get; } = traceParent;
        public string? TraceState { get; } = traceState;
        public IEnumerable<KeyValuePair<string, string>> Baggage { get; } = baggage;
    }
}

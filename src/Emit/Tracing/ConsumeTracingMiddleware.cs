namespace Emit.Tracing;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Abstractions.Tracing;
using Microsoft.Extensions.Options;

/// <summary>
/// Middleware that starts the <c>process</c> span of each consumer entry, outermost in its pipeline so the span
/// covers the whole processing of the message: its retries and what is done when it fails. A single message's span
/// has the message's creation context as its parent, and links to it. A batch can't have one parent: its span
/// links to the creation context of each of its messages. Consumer identity is baked in at build time by
/// <see cref="Pipeline.ConsumerPipelineComposer{TValue}"/>.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
internal sealed class ConsumeTracingMiddleware<TMessage>(
    IOptions<EmitTracingOptions> options,
    ActivityEnricherInvoker enricherInvoker,
    INodeIdentity nodeIdentity,
    string consumerIdentifier,
    Type? consumerType) : IMiddleware<ConsumeContext<TMessage>>
{
    // The type of the message, or of each message of a batch
    private static readonly string MessageTypeName =
        typeof(TMessage).IsGenericType && typeof(TMessage).GetGenericTypeDefinition() == typeof(MessageBatch<>)
            ? typeof(TMessage).GetGenericArguments()[0].Name
            : typeof(TMessage).Name;

    private readonly EmitTracingOptions options = options.Value;

    public async Task InvokeAsync(
        ConsumeContext<TMessage> context,
        IMiddlewarePipeline<ConsumeContext<TMessage>> next)
    {
        if (!MessagingActivity.IsEnabled(EmitActivitySources.Consumer, options))
        {
            await next.InvokeAsync(context).ConfigureAwait(false);
            return;
        }

        List<KeyValuePair<string, object?>> tags =
        [
            .. context.TransportContext.GetTracingTags(),
            new(ActivityTagNames.NodeId, nodeIdentity.NodeId.ToString()),
            new(ActivityTagNames.MessageType, MessageTypeName),
            new(ActivityTagNames.Consumer, consumerIdentifier),
        ];

        if (consumerType is not null)
        {
            tags.Add(new(ActivityTagNames.ConsumerType, consumerType.Name));
        }

        ActivityContext parent = default;
        List<ActivityLink> links;

        if (context.Message is IBatchMessage batch)
        {
            links = [.. batch.GetItemTransportContexts()
                .Select(item => MessagingActivity.CreationContext(item.Headers))
                .Where(creationContext => creationContext != default)
                .Select(creationContext => new ActivityLink(creationContext))];
            tags.Add(new(ActivityTagNames.MessagingBatchMessageCount, batch.Count));
        }
        else
        {
            parent = MessagingActivity.CreationContext(context.Headers);
            links = parent != default ? [new ActivityLink(parent)] : [];
        }

        using var activity = MessagingActivity.Start(
            EmitActivitySources.Consumer,
            options,
            ActivityNames.Process,
            ActivityKind.Consumer,
            context.DestinationAddress,
            context.Message is IBatchMessage ? null : context.MessageId,
            parent,
            links,
            tags);

        if (activity is null)
        {
            await next.InvokeAsync(context).ConfigureAwait(false);
            return;
        }

        if (context.Message is not IBatchMessage)
        {
            MessagingActivity.RestoreBaggage(activity, context.Headers, options);
        }

        enricherInvoker.InvokeEnrichers(
            activity,
            new EnrichmentContext
            {
                MessageContext = context,
                Phase = EnrichmentPhases.Consume
            },
            context.Services);

        // A failure is recorded on the span by the error handling inside it, which decides it is final
        await next.InvokeAsync(context).ConfigureAwait(false);
    }
}

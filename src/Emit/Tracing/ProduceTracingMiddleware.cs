namespace Emit.Tracing;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Abstractions.Tracing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Middleware that starts the <c>create</c> span of an outbound message and makes it the message's creation
/// context: its W3C trace context (traceparent, tracestate, baggage) goes into
/// <see cref="SendContext{T}.Headers"/>, so whatever sends and consumes the message joins its trace.
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
        if (!MessagingActivity.IsEnabled(EmitActivitySources.Producer, options))
        {
            await next.InvokeAsync(context).ConfigureAwait(false);
            return;
        }

        using var activity = MessagingActivity.Start(
            EmitActivitySources.Producer,
            options,
            ActivityNames.Create,
            ActivityKind.Producer,
            context.DestinationAddress,
            context.MessageId,
            tags:
            [
                new(ActivityTagNames.NodeId, nodeIdentity.NodeId.ToString()),
                new(ActivityTagNames.MessageType, typeof(TMessage).Name),
            ]);

        if (activity is null)
        {
            await next.InvokeAsync(context).ConfigureAwait(false);
            return;
        }

        MessagingActivity.Inject(activity, context.Headers, options, logger);

        enricherInvoker.InvokeEnrichers(
            activity,
            new EnrichmentContext
            {
                MessageContext = context,
                Phase = EnrichmentPhases.Produce
            },
            context.Services);

        try
        {
            await next.InvokeAsync(context).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            MessagingActivity.RecordFailure(activity, exception, context.CancellationToken);
            throw;
        }
    }
}

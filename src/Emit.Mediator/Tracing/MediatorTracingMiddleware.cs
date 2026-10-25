namespace Emit.Mediator.Tracing;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Abstractions.Tracing;
using Emit.Tracing;
using Microsoft.Extensions.Options;

/// <summary>
/// Inbound middleware that starts a span for each mediator request, named after the request type, covering the
/// whole dispatch (a stream request's span lasts as long as its stream). Messages produced while handling the
/// request join its trace. Auto-inserted as the outermost layer of the mediator pipeline.
/// </summary>
/// <typeparam name="TMessage">The request type.</typeparam>
internal sealed class MediatorTracingMiddleware<TMessage>(
    IOptions<EmitTracingOptions> options,
    ActivityEnricherInvoker enricherInvoker,
    INodeIdentity nodeIdentity) : IMiddleware<MediatorContext<TMessage>>
{
    private static readonly ActivitySource Source = new(EmitActivitySourceNames.Mediator, EmitActivitySources.Version);
    private static readonly string RequestTypeName = typeof(TMessage).Name;

    /// <inheritdoc />
    public async Task InvokeAsync(MediatorContext<TMessage> context, Func<Task> next)
    {
        using var activity = MessagingActivity.StartActivity(
            Source,
            options.Value,
            RequestTypeName,
            ActivityKind.Internal,
            tags: [new(ActivityTagNames.NodeId, nodeIdentity.NodeId.ToString())]);

        if (activity is not null)
        {
            enricherInvoker.InvokeEnrichers(
                activity,
                new EnrichmentContext
                {
                    MessageContext = context,
                    Phase = EnrichmentPhases.Mediator
                },
                context.Services);
        }

        try
        {
            await next().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            MessagingActivity.RecordFailure(activity, exception, context.CancellationToken);
            throw;
        }
    }
}

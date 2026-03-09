namespace Emit.Pipeline;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Models;
using Emit.Observability;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Builds a terminal delegate that enqueues messages to the transactional outbox.
/// Handles transaction validation, trace context propagation, repository enqueue, and
/// observer notification. Providers supply a delegate that serializes the payload
/// and populates provider-specific entry fields.
/// </summary>
public static class OutboxTerminalBuilder
{
    /// <summary>
    /// Creates an outbox terminal delegate.
    /// </summary>
    /// <typeparam name="TValue">The message value type.</typeparam>
    /// <param name="createEntry">
    /// Provider-specific delegate that serializes the message and returns a partially-populated
    /// <see cref="OutboxEntry"/>. The builder sets <see cref="OutboxEntry.TraceParent"/>,
    /// <see cref="OutboxEntry.TraceState"/>, and <see cref="OutboxEntry.EnqueuedAt"/>
    /// after the delegate returns.
    /// </param>
    /// <returns>A terminal pipeline that enqueues entries to the outbox repository.</returns>
    public static IMiddlewarePipeline<SendContext<TValue>> Build<TValue>(
        Func<SendContext<TValue>, CancellationToken, Task<OutboxEntry>> createEntry)
    {
        return new OutboxTerminalPipeline<TValue>(createEntry);
    }

    private sealed class OutboxTerminalPipeline<TValue>(
        Func<SendContext<TValue>, CancellationToken, Task<OutboxEntry>> createEntry)
        : IMiddlewarePipeline<SendContext<TValue>>
    {
        public async Task InvokeAsync(SendContext<TValue> context)
        {
            var repository = context.Services.GetRequiredService<IOutboxRepository>();
            var emitContext = context.Services.GetRequiredService<IEmitContext>();

            if (emitContext.Transaction is null)
            {
                throw new InvalidOperationException("No transaction context is available.");
            }

            // Read trace context from headers (injected by ProduceTracingMiddleware)
            string? traceParent = null;
            string? traceState = null;
            foreach (var header in context.Headers)
            {
                if (header.Key == WellKnownHeaders.TraceParent)
                    traceParent = header.Value;
                else if (header.Key == WellKnownHeaders.TraceState)
                    traceState = header.Value;
            }

            var entry = await createEntry(context, context.CancellationToken).ConfigureAwait(false);

            entry.TraceParent = traceParent;
            entry.TraceState = traceState;
            entry.EnqueuedAt = context.Timestamp.UtcDateTime;

            await repository.EnqueueAsync(entry, context.CancellationToken).ConfigureAwait(false);

            var observerInvoker = context.Services.GetService<OutboxObserverInvoker>();
            if (observerInvoker is not null)
            {
                await observerInvoker.OnEnqueuedAsync(entry, context.CancellationToken).ConfigureAwait(false);
            }
        }
    }
}

namespace Emit.Mediator;

using Emit.Abstractions.Pipeline;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Terminal adapter that bridges from the typed pipeline to a stream handler. Resolves the
/// handler and advances its sequence one item at a time, as the consumer asks for them.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The type of each item in the produced sequence.</typeparam>
/// <remarks>
/// Enumeration happens here rather than after the pipeline returns, so the handler's work is
/// enclosed by every middleware above this terminal for the whole life of the stream. The
/// handler's enumerator is also disposed here, which puts its cleanup inside the same unit of
/// work as the items it produced.
/// </remarks>
internal sealed class MediatorStreamHandlerInvoker<TRequest, TResponse>(Type handlerType)
    : IMiddlewarePipeline<MediatorContext<TRequest>>
    where TRequest : IStreamRequest<TResponse>
{
    /// <inheritdoc />
    public async Task InvokeAsync(MediatorContext<TRequest> context)
    {
        var handler = (IStreamRequestHandler<TRequest, TResponse>)context.Services.GetRequiredService(handlerType);

        var sink = context.Features.Get<IStreamSink<TResponse>>()
            ?? throw new InvalidOperationException(
                $"No stream sink is available on this context. " +
                $"'{typeof(TRequest).Name}' must be dispatched through {nameof(IMediator)}.{nameof(IMediator.CreateStreamAsync)}.");

        var cancellationToken = context.CancellationToken;
        var enumerator = handler.HandleAsync(context.Message, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        await using (enumerator.ConfigureAwait(false))
        {
            // The handler is advanced only once the consumer has asked for an item, so it is
            // never left computing while the consumer decides whether to continue.
            while (await sink.WaitForDemandAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    break;
                }

                await sink.WriteAsync(enumerator.Current, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

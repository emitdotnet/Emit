namespace Emit.Observability;

using Emit.Abstractions;
using Emit.Abstractions.Observability;
using Emit.Abstractions.Pipeline;
using Microsoft.Extensions.Logging;

/// <summary>
/// Internal middleware that invokes all registered <see cref="IProduceObserver"/> instances
/// around the outbound pipeline. Auto-inserted as the outermost layer.
/// </summary>
internal sealed class ProduceObserverMiddleware<TMessage>(
    IEnumerable<IProduceObserver> observers,
    ILogger<ProduceObserverMiddleware<TMessage>> logger) : IMiddleware<SendContext<TMessage>>
{
    private readonly IProduceObserver[] observers = observers.ToArray();

    /// <inheritdoc />
    public async Task InvokeAsync(SendContext<TMessage> context, IMiddlewarePipeline<SendContext<TMessage>> next)
    {
        if (this.observers is [])
        {
            await next.InvokeAsync(context).ConfigureAwait(false);
            return;
        }

        foreach (var observer in this.observers)
        {
            try
            {
                await observer.OnProducingAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, $"{nameof(IProduceObserver)}.{nameof(IProduceObserver.OnProducingAsync)} failed for {{ObserverType}}", observer.GetType().Name);
            }
        }

        try
        {
            await next.InvokeAsync(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            foreach (var observer in this.observers)
            {
                try
                {
                    await observer.OnProduceErrorAsync(context, ex).ConfigureAwait(false);
                }
                catch (Exception observerEx)
                {
                    logger.LogWarning(observerEx, $"{nameof(IProduceObserver)}.{nameof(IProduceObserver.OnProduceErrorAsync)} failed for {{ObserverType}}", observer.GetType().Name);
                }
            }

            throw;
        }

        foreach (var observer in this.observers)
        {
            try
            {
                await observer.OnProducedAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, $"{nameof(IProduceObserver)}.{nameof(IProduceObserver.OnProducedAsync)} failed for {{ObserverType}}", observer.GetType().Name);
            }
        }
    }
}

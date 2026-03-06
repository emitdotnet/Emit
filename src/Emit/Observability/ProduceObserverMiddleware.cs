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
    ILogger<ProduceObserverMiddleware<TMessage>> logger) : IMiddleware<OutboundContext<TMessage>>
{
    private readonly IProduceObserver[] observers = observers.ToArray();

    /// <inheritdoc />
    public async Task InvokeAsync(OutboundContext<TMessage> context, MessageDelegate<OutboundContext<TMessage>> next)
    {
        if (this.observers is [])
        {
            await next(context).ConfigureAwait(false);
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
                logger.LogWarning(ex, "IProduceObserver.OnProducingAsync failed for {ObserverType}", observer.GetType().Name);
            }
        }

        try
        {
            await next(context).ConfigureAwait(false);
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
                    logger.LogWarning(observerEx, "IProduceObserver.OnProduceErrorAsync failed for {ObserverType}", observer.GetType().Name);
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
                logger.LogWarning(ex, "IProduceObserver.OnProducedAsync failed for {ObserverType}", observer.GetType().Name);
            }
        }
    }
}

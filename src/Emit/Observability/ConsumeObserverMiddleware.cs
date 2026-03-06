namespace Emit.Observability;

using Emit.Abstractions;
using Emit.Abstractions.Observability;
using Emit.Abstractions.Pipeline;
using Microsoft.Extensions.Logging;

/// <summary>
/// Internal middleware that invokes all registered <see cref="IConsumeObserver"/> instances
/// around the inbound pipeline. Auto-inserted as the outermost layer for transport consumers.
/// Not inserted into the mediator pipeline.
/// </summary>
internal sealed class ConsumeObserverMiddleware<TMessage>(
    IEnumerable<IConsumeObserver> observers,
    ILogger<ConsumeObserverMiddleware<TMessage>> logger) : IMiddleware<InboundContext<TMessage>>
{
    private readonly IConsumeObserver[] observers = observers.ToArray();

    /// <inheritdoc />
    public async Task InvokeAsync(InboundContext<TMessage> context, MessageDelegate<InboundContext<TMessage>> next)
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
                await observer.OnConsumingAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "IConsumeObserver.OnConsumingAsync failed for {ObserverType}", observer.GetType().Name);
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
                    await observer.OnConsumeErrorAsync(context, ex).ConfigureAwait(false);
                }
                catch (Exception observerEx)
                {
                    logger.LogWarning(observerEx, "IConsumeObserver.OnConsumeErrorAsync failed for {ObserverType}", observer.GetType().Name);
                }
            }

            throw;
        }

        foreach (var observer in this.observers)
        {
            try
            {
                await observer.OnConsumedAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "IConsumeObserver.OnConsumedAsync failed for {ObserverType}", observer.GetType().Name);
            }
        }
    }
}

namespace Emit.Mediator.Observability;

using Emit.Abstractions.Pipeline;
using Microsoft.Extensions.Logging;

/// <summary>
/// Internal middleware that invokes all registered <see cref="IMediatorObserver"/> instances
/// around the mediator inbound pipeline. Auto-inserted as the outermost layer at the mediator level.
/// </summary>
internal sealed class MediatorObserverMiddleware<TMessage>(
    IEnumerable<IMediatorObserver> observers,
    ILogger<MediatorObserverMiddleware<TMessage>> logger) : IMiddleware<MediatorContext<TMessage>>
{
    private readonly IMediatorObserver[] observers = observers.ToArray();

    /// <inheritdoc />
    public async Task InvokeAsync(MediatorContext<TMessage> context, IMiddlewarePipeline<MediatorContext<TMessage>> next)
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
                await observer.OnHandlingAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, $"{nameof(IMediatorObserver)}.{nameof(IMediatorObserver.OnHandlingAsync)} failed for {{ObserverType}}", observer.GetType().Name);
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
                    await observer.OnHandleErrorAsync(context, ex).ConfigureAwait(false);
                }
                catch (Exception observerEx)
                {
                    logger.LogWarning(observerEx, $"{nameof(IMediatorObserver)}.{nameof(IMediatorObserver.OnHandleErrorAsync)} failed for {{ObserverType}}", observer.GetType().Name);
                }
            }

            throw;
        }

        foreach (var observer in this.observers)
        {
            try
            {
                await observer.OnHandledAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, $"{nameof(IMediatorObserver)}.{nameof(IMediatorObserver.OnHandledAsync)} failed for {{ObserverType}}", observer.GetType().Name);
            }
        }
    }
}

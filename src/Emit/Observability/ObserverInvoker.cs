namespace Emit.Observability;

using Emit.Abstractions.Observability;
using Microsoft.Extensions.Logging;

/// <summary>
/// Extension method for invoking all observers with isolated error handling.
/// </summary>
internal static class ObserverInvoker
{
    /// <summary>
    /// Invokes <paramref name="action"/> on every observer, catching and logging
    /// individual failures so that one observer never blocks the rest.
    /// </summary>
    internal static async Task InvokeAllAsync<TObserver>(
        this TObserver[] observers,
        Func<TObserver, Task> action,
        ILogger logger)
        where TObserver : IObserver
    {
        foreach (var observer in observers)
        {
            try
            {
                await action(observer).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Observer {ObserverType} failed", observer.GetType().Name);
            }
        }
    }
}

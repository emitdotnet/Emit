namespace Emit.Abstractions.Observability;
/// <summary>
/// Observes the lifecycle of outbound (produce) operations across all transport providers.
/// Implement this interface to hook into produce events globally — for metrics, tracing,
/// logging, or integration test assertions.
/// </summary>
/// <remarks>
/// <para>
/// Observers are invoked by internal middleware that wraps the outermost layer of the
/// outbound pipeline. All registered observers run for every produce operation regardless
/// of the underlying transport provider. To identify the provider, inspect
/// <c>context.Features</c> for provider-specific feature interfaces.
/// </para>
/// <para>
/// All methods have default implementations that return <see cref="Task.CompletedTask"/>,
/// so implementors only need to override the callbacks they care about.
/// </para>
/// <para>
/// Observer exceptions are caught and logged individually — a failing observer never
/// blocks other observers or interrupts the pipeline.
/// </para>
/// </remarks>
public interface IProduceObserver : IObserver
{
    /// <summary>
    /// Called before the outbound pipeline executes.
    /// </summary>
    /// <typeparam name="T">The message type being produced.</typeparam>
    /// <param name="context">The outbound pipeline context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnProducingAsync<T>(SendContext<T> context) => Task.CompletedTask;

    /// <summary>
    /// Called after the outbound pipeline completes successfully.
    /// </summary>
    /// <typeparam name="T">The message type that was produced.</typeparam>
    /// <param name="context">The outbound pipeline context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnProducedAsync<T>(SendContext<T> context) => Task.CompletedTask;

    /// <summary>
    /// Called when the outbound pipeline throws an exception.
    /// </summary>
    /// <typeparam name="T">The message type that failed to produce.</typeparam>
    /// <param name="context">The outbound pipeline context.</param>
    /// <param name="exception">The exception thrown by the pipeline.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnProduceErrorAsync<T>(SendContext<T> context, Exception exception) => Task.CompletedTask;
}

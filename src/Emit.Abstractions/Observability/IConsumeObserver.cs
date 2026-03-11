namespace Emit.Abstractions.Observability;
/// <summary>
/// Observes the lifecycle of inbound (consume) operations across all transport providers.
/// Implement this interface to hook into consume events globally — for metrics, tracing,
/// logging, or integration test assertions.
/// </summary>
/// <remarks>
/// <para>
/// Observers are invoked by internal middleware that wraps the outermost layer of the
/// inbound pipeline for transport consumers (e.g., Kafka). This observer does not fire
/// for mediator requests — use <c>IMediatorObserver</c> for in-process request/response
/// observation.
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
public interface IConsumeObserver : IObserver
{
    /// <summary>
    /// Called before the inbound pipeline executes.
    /// </summary>
    /// <typeparam name="T">The message type being consumed.</typeparam>
    /// <param name="context">The inbound pipeline context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnConsumingAsync<T>(ConsumeContext<T> context) => Task.CompletedTask;

    /// <summary>
    /// Called after the inbound pipeline completes successfully.
    /// </summary>
    /// <typeparam name="T">The message type that was consumed.</typeparam>
    /// <param name="context">The inbound pipeline context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnConsumedAsync<T>(ConsumeContext<T> context) => Task.CompletedTask;

    /// <summary>
    /// Called when the inbound pipeline throws an exception.
    /// </summary>
    /// <typeparam name="T">The message type that failed to consume.</typeparam>
    /// <param name="context">The inbound pipeline context.</param>
    /// <param name="exception">The exception thrown by the pipeline.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnConsumeErrorAsync<T>(ConsumeContext<T> context, Exception exception) => Task.CompletedTask;
}

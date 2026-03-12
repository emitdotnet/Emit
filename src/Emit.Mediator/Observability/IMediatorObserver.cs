namespace Emit.Mediator.Observability;

/// <summary>
/// Observes the lifecycle of mediator request handling operations.
/// Implement this interface for mediator-specific metrics, tracing, logging,
/// or integration test assertions.
/// </summary>
/// <remarks>
/// <para>
/// Observers are invoked by internal middleware that wraps the outermost layer of the
/// mediator inbound pipeline. This observer fires only for mediator requests — not for
/// transport consumer operations. Use <see cref="Abstractions.Observability.IConsumeObserver"/>
/// for transport-level consumption observation.
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
public interface IMediatorObserver
{
    /// <summary>
    /// Called before the mediator inbound pipeline executes for a request.
    /// </summary>
    /// <typeparam name="T">The request type being handled.</typeparam>
    /// <param name="context">The mediator pipeline context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnHandlingAsync<T>(MediatorContext<T> context) => Task.CompletedTask;

    /// <summary>
    /// Called after the mediator inbound pipeline completes successfully.
    /// </summary>
    /// <typeparam name="T">The request type that was handled.</typeparam>
    /// <param name="context">The mediator pipeline context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnHandledAsync<T>(MediatorContext<T> context) => Task.CompletedTask;

    /// <summary>
    /// Called when the mediator inbound pipeline throws an exception.
    /// </summary>
    /// <typeparam name="T">The request type that failed to handle.</typeparam>
    /// <param name="context">The mediator pipeline context.</param>
    /// <param name="exception">The exception thrown by the pipeline.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnHandleErrorAsync<T>(MediatorContext<T> context, Exception exception) => Task.CompletedTask;
}

namespace Emit.Abstractions;

/// <summary>
/// Scoped context for the current Emit operation, providing access to the active transaction
/// and to callbacks that run once the work of the current consumer or handler is final.
/// </summary>
/// <remarks>
/// Registered by <c>AddEmit</c> and resolved in a scope.
/// </remarks>
public interface IEmitContext
{
    /// <summary>
    /// Gets or sets the transaction context for the current scope.
    /// </summary>
    /// <remarks>
    /// Set when a transaction begins through <see cref="IUnitOfWork"/> or
    /// <see cref="TransactionalAttribute"/>, and cleared when it ends. Messages produced through
    /// the outbox while it is set are enqueued in the same transaction as your business data writes.
    /// </remarks>
    ITransactionContext? Transaction { get; set; }

    /// <summary>
    /// Registers a callback that runs once the work of the current consumer or handler has
    /// succeeded: after its transaction commits when it is decorated with
    /// <see cref="TransactionalAttribute"/>, or after it completes otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use it for side effects that must never be observed before the work is durable, such as
    /// real-time notifications or cache invalidation. The callback runs once, after any retries,
    /// and never when the work fails. Callbacks run in registration order.
    /// </para>
    /// <para>
    /// The callback is not durable: it does not run if the process stops before it is reached,
    /// and it carries none of the delivery guarantees of the outbox. A consumed message that is
    /// delivered again is processed again, and registers its callbacks again. A callback that
    /// throws is logged and does not change the outcome of the work.
    /// </para>
    /// <para>
    /// A consumer or handler called while another one's transaction is open in the same scope
    /// writes into that transaction, so its callbacks run when that transaction commits.
    /// Transactions begun manually with <see cref="IUnitOfWork"/>, or committed with
    /// <c>SaveChangesAsync</c>, are not tracked: callbacks follow the outcome of the consumer
    /// or handler, not of those transactions.
    /// </para>
    /// </remarks>
    /// <param name="callback">
    /// The callback, receiving the cancellation token of the message or request.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no consumer or handler is running, or when its work has already completed.
    /// </exception>
    void OnSucceeded(Func<CancellationToken, Task> callback);

    /// <summary>
    /// Registers a callback that runs once the work of the current consumer or handler has
    /// definitely failed: when it throws and no retry follows, including when it is cancelled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The callback runs once, after the transaction has rolled back and before the error policy
    /// dead-letters or discards the message. A failed attempt that is retried does not run it;
    /// the next attempt registers its own callbacks. Callbacks run in registration order.
    /// </para>
    /// <para>
    /// The callback is not durable and carries none of the delivery guarantees of the outbox.
    /// A consumer stopped by shutdown runs it with an <see cref="OperationCanceledException"/>
    /// and an already cancelled token; the message is delivered again after restart and
    /// processed as new. A callback that throws is logged and does not change the outcome.
    /// </para>
    /// <para>
    /// Transactions begun manually with <see cref="IUnitOfWork"/>, or committed with
    /// <c>SaveChangesAsync</c>, are not tracked: callbacks follow the outcome of the consumer
    /// or handler, not of those transactions.
    /// </para>
    /// </remarks>
    /// <param name="callback">
    /// The callback, receiving the exception that failed the work and the cancellation token of
    /// the message or request.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no consumer or handler is running, or when its work has already completed.
    /// </exception>
    void OnFailed(Func<Exception, CancellationToken, Task> callback);
}

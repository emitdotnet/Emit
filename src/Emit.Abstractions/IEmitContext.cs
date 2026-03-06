namespace Emit.Abstractions;

/// <summary>
/// Scoped context for the current Emit operation, providing access to the active transaction.
/// </summary>
/// <remarks>
/// Register via <c>AddEmit</c> and resolve in a scope. Set <see cref="Transaction"/> before
/// calling <c>ProduceAsync</c> when using outbox mode.
/// </remarks>
public interface IEmitContext
{
    /// <summary>
    /// Gets or sets the transaction context for the current scope.
    /// </summary>
    /// <remarks>
    /// Required when outbox mode is enabled. The transaction ensures atomicity between
    /// your business data writes and the outbox enqueue operation.
    /// </remarks>
    ITransactionContext? Transaction { get; set; }
}

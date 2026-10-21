namespace Emit;

using Emit.Abstractions;
using Emit.Middleware;

/// <inheritdoc />
internal sealed class EmitContext : IEmitContext
{
    private ITransactionContext? transaction;

    /// <inheritdoc />
    public ITransactionContext? Transaction
    {
        get => transaction;
        set
        {
            // Prevent overwriting an active transaction with a different one
            if (transaction is not null && value is not null && !ReferenceEquals(transaction, value))
            {
                throw new InvalidOperationException(
                    $"A transaction is already active on this context. " +
                    $"If the handler is decorated with [{nameof(TransactionalAttribute)}], " +
                    $"remove the manual {nameof(IUnitOfWork)}.{nameof(IUnitOfWork.BeginAsync)} call.");
            }
            transaction = value;
        }
    }

    /// <inheritdoc />
    public void OnSucceeded(Func<CancellationToken, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        GetCurrentUnitOfWork().AddSucceeded(callback);
    }

    /// <inheritdoc />
    public void OnFailed(Func<Exception, CancellationToken, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        GetCurrentUnitOfWork().AddFailed(callback);
    }

    private static UnitOfWorkCallbacks GetCurrentUnitOfWork() =>
        UnitOfWorkCallbacks.Current
            ?? throw new InvalidOperationException(
                $"No consumer or handler is running, so a callback registered with " +
                $"{nameof(IEmitContext.OnSucceeded)} or {nameof(IEmitContext.OnFailed)} would never run. " +
                "Register callbacks from a consumer, a mediator handler, or their middleware.");
}

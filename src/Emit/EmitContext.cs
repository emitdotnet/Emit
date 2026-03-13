namespace Emit;

using Emit.Abstractions;

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
}

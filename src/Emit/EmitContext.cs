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
                    "Cannot overwrite an active transaction. " +
                    "Commit or rollback the existing transaction before setting a new one.");
            }
            transaction = value;
        }
    }
}

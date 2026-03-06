namespace BuildingSentinel.Common.Transactions;

using Emit.Abstractions;

/// <summary>
/// Creates a provider-specific transaction context and wires it into the supplied <see cref="IEmitContext"/> for use by the transactional outbox.
/// </summary>
public interface ITransactionFactory
{
    /// <summary>
    /// Begins a new transaction, assigns it to <paramref name="emitContext"/>, and returns the context for committing or rolling back.
    /// </summary>
    Task<ITransactionContext> BeginTransactionAsync(
        IEmitContext emitContext,
        CancellationToken cancellationToken = default);
}

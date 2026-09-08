namespace Emit.IntegrationTests.Integration;

using System.Collections.Concurrent;
using Emit.Abstractions;

/// <summary>
/// A single observation of the ambient transaction state, captured from inside a handler.
/// </summary>
/// <param name="Transaction">
/// The ambient transaction at the moment the handler ran, or <see langword="null"/> if none was active.
/// </param>
/// <param name="ProviderSession">
/// The provider's own session handle at that moment, when the provider exposes one
/// (MongoDB does, via <c>IMongoSessionAccessor</c>). <see langword="null"/> otherwise.
/// </param>
public sealed record TransactionObservation(ITransactionContext? Transaction, object? ProviderSession);

/// <summary>
/// Thread-safe recorder for ambient transaction state observed inside handlers.
/// Register as a singleton in the test's service collection.
/// </summary>
/// <remarks>
/// Mediator handlers receive only the request and a cancellation token, so they cannot see the
/// pipeline context. This probe is how an observation travels from inside a handler back to
/// the test. Recording the <see cref="ITransactionContext"/> itself, rather than a boolean,
/// lets a test assert both that a transaction existed and how it was resolved
/// (<see cref="ITransactionContext.IsCommitted"/> / <see cref="ITransactionContext.IsRolledBack"/>)
/// after the request completes.
/// </remarks>
public sealed class TransactionProbe
{
    private readonly ConcurrentQueue<TransactionObservation> observations = new();

    /// <summary>Gets every observation recorded so far, in the order they were recorded.</summary>
    public IReadOnlyCollection<TransactionObservation> Observations => observations;

    /// <summary>Gets the number of observations recorded so far.</summary>
    public int Count => observations.Count;

    /// <summary>Gets the most recent observation, or <see langword="null"/> if none were recorded.</summary>
    public TransactionObservation? Last => observations.LastOrDefault();

    /// <summary>Records the ambient transaction state observed inside a handler.</summary>
    /// <param name="transaction">The ambient transaction, or <see langword="null"/> if none is active.</param>
    /// <param name="providerSession">The provider's session handle, when it exposes one.</param>
    public void Record(ITransactionContext? transaction, object? providerSession = null)
        => observations.Enqueue(new TransactionObservation(transaction, providerSession));
}

/// <summary>
/// Exposes the persistence provider's own ambient session handle so provider-agnostic tests can
/// assert on it without referencing provider types.
/// </summary>
public interface IAmbientSessionInspector
{
    /// <summary>
    /// Gets the provider's active session handle, or <see langword="null"/> when the provider
    /// exposes no such concept or no transaction is in progress.
    /// </summary>
    object? CurrentSession { get; }
}

/// <summary>
/// Default inspector for providers that expose no ambient session handle.
/// </summary>
public sealed class NullAmbientSessionInspector : IAmbientSessionInspector
{
    /// <inheritdoc />
    public object? CurrentSession => null;
}

namespace Emit.Middleware;

using Emit.Abstractions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Callbacks registered during one unit of work, run once when its outcome is final. Registrations
/// made through <see cref="IEmitContext"/> go to the unit of work that is current for the async flow,
/// so units of work running concurrently in one DI scope never receive each other's callbacks.
/// </summary>
internal sealed class UnitOfWorkCallbacks
{
    private static readonly AsyncLocal<UnitOfWorkCallbacks?> current = new();

    /// <summary>
    /// Gets or sets the unit of work current for the async flow. A value set inside an async
    /// method flows to everything it awaits and is not seen by its caller once it returns.
    /// </summary>
    internal static UnitOfWorkCallbacks? Current
    {
        get => current.Value;
        set => current.Value = value;
    }

    /// <summary>
    /// Gets or sets the transaction this unit of work opened, if any. A unit of work nested inside
    /// it in the same scope writes into that transaction, so it joins this one instead of running
    /// its callbacks on its own.
    /// </summary>
    internal ITransactionContext? Transaction { get; set; }

    private readonly object sync = new();
    private readonly List<Func<CancellationToken, Task>> succeeded = [];
    private readonly List<Func<Exception, CancellationToken, Task>> failed = [];
    private bool completed;

    /// <summary>
    /// Returns <see langword="true"/> when a unit of work starting now in a scope whose active
    /// transaction is <paramref name="transaction"/> belongs to this one.
    /// </summary>
    internal bool Owns(ITransactionContext? transaction)
    {
        lock (sync)
        {
            return !completed && transaction is not null && ReferenceEquals(Transaction, transaction);
        }
    }

    internal void AddSucceeded(Func<CancellationToken, Task> callback)
    {
        lock (sync)
        {
            ThrowIfCompleted();
            succeeded.Add(callback);
        }
    }

    internal void AddFailed(Func<Exception, CancellationToken, Task> callback)
    {
        lock (sync)
        {
            ThrowIfCompleted();
            failed.Add(callback);
        }
    }

    /// <summary>
    /// Completes the unit of work without running anything, because another attempt follows.
    /// </summary>
    internal void Discard()
    {
        lock (sync)
        {
            completed = true;
        }
    }

    /// <summary>
    /// Completes the unit of work and runs its success callbacks in registration order.
    /// </summary>
    internal async Task SucceedAsync(ILogger logger, CancellationToken cancellationToken)
    {
        foreach (var callback in Complete(succeeded))
        {
            try
            {
                await callback(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"An {nameof(IEmitContext.OnSucceeded)} callback failed");
            }
        }
    }

    /// <summary>
    /// Completes the unit of work and runs its failure callbacks in registration order.
    /// </summary>
    internal async Task FailAsync(Exception exception, ILogger logger, CancellationToken cancellationToken)
    {
        foreach (var callback in Complete(failed))
        {
            try
            {
                await callback(exception, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"An {nameof(IEmitContext.OnFailed)} callback failed");
            }
        }
    }

    private List<T> Complete<T>(List<T> callbacks)
    {
        // Marked complete before anything runs: a callback registering another one would otherwise
        // add to the list being run, and nothing would ever run what it added.
        lock (sync)
        {
            completed = true;
            return [.. callbacks];
        }
    }

    private void ThrowIfCompleted()
    {
        if (completed)
        {
            throw new InvalidOperationException(
                "The unit of work has already completed, so the callback would never run. " +
                $"Register {nameof(IEmitContext.OnSucceeded)} and {nameof(IEmitContext.OnFailed)} callbacks " +
                "while the consumer or handler is still running.");
        }
    }
}

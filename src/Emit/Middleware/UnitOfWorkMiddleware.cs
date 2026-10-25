namespace Emit.Middleware;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Middleware that bounds the unit of work of one consumer or handler. It wraps the handler and
/// the handler's own middleware, opens a transaction when the handler is decorated with
/// <see cref="TransactionalAttribute"/>, and runs the callbacks registered through
/// <see cref="IEmitContext"/> once the outcome is final: after the commit, or after a failure
/// that no further attempt follows.
/// </summary>
/// <typeparam name="TContext">The pipeline context type.</typeparam>
internal sealed class UnitOfWorkMiddleware<TContext>(
    Type handlerType,
    ILogger<UnitOfWorkMiddleware<TContext>> logger) : IMiddleware<TContext>
    where TContext : MessageContext
{
    private readonly bool isTransactional = Attribute.IsDefined(handlerType, typeof(TransactionalAttribute));

    /// <inheritdoc />
    public async Task InvokeAsync(TContext context, Func<Task> next)
    {
        var emitContext = context.Services.GetRequiredService<IEmitContext>();

        // Inside a transaction that an enclosing unit of work opened in this scope, this one writes
        // into that transaction, so the enclosing unit of work owns the outcome and the callbacks.
        if (UnitOfWorkCallbacks.Current is { } enclosing && enclosing.Owns(emitContext.Transaction))
        {
            await ExecuteAsync(context, next, emitContext, callbacks: null).ConfigureAwait(false);
            return;
        }

        var callbacks = new UnitOfWorkCallbacks();
        UnitOfWorkCallbacks.Current = callbacks;

        try
        {
            await ExecuteAsync(context, next, emitContext, callbacks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Another attempt registers its own callbacks, so this attempt's are only run when
            // nothing follows it.
            if (context.WillRetry?.Invoke(ex) == true)
            {
                callbacks.Discard();
            }
            else
            {
                await callbacks.FailAsync(ex, logger, context.CancellationToken).ConfigureAwait(false);
            }

            throw;
        }

        await callbacks.SucceedAsync(logger, context.CancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteAsync(
        TContext context,
        Func<Task> next,
        IEmitContext emitContext,
        UnitOfWorkCallbacks? callbacks)
    {
        if (!isTransactional)
        {
            await next().ConfigureAwait(false);
            return;
        }

        var unitOfWork = context.Services.GetRequiredService<IUnitOfWork>();
        await using var transaction = await unitOfWork
            .BeginAsync(context.CancellationToken)
            .ConfigureAwait(false);

        callbacks?.Transaction = emitContext.Transaction;

        await next().ConfigureAwait(false);

        // Use CancellationToken.None so a shutdown signal racing with a successful handler
        // cannot silently discard committed work by cancelling the commit.
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
    }
}

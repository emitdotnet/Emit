namespace Emit.Middleware;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Middleware that wraps handler invocation in a unit-of-work transaction when the handler
/// is decorated with <see cref="TransactionalAttribute"/>. If the attribute is absent, the
/// middleware delegates to the next pipeline stage without opening a transaction.
/// </summary>
/// <typeparam name="TContext">The pipeline context type.</typeparam>
internal sealed class TransactionalOutboxMiddleware<TContext>(
    Type handlerType) : IMiddleware<TContext>
    where TContext : MessageContext
{
    private readonly bool isTransactional = Attribute.IsDefined(handlerType, typeof(TransactionalAttribute));

    /// <inheritdoc />
    public async Task InvokeAsync(TContext context, IMiddlewarePipeline<TContext> next)
    {
        if (!isTransactional)
        {
            await next.InvokeAsync(context).ConfigureAwait(false);
            return;
        }

        var unitOfWork = context.Services.GetRequiredService<IUnitOfWork>();
        await using var transaction = await unitOfWork
            .BeginAsync(context.CancellationToken)
            .ConfigureAwait(false);

        await next.InvokeAsync(context).ConfigureAwait(false);

        // Use CancellationToken.None so a shutdown signal racing with a successful handler
        // cannot silently discard committed work by cancelling the commit.
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
    }
}

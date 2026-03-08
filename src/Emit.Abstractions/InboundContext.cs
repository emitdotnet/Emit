namespace Emit.Abstractions;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Context for all inbound (consumer/handler) pipelines.
/// Transport-specific metadata is carried via features on <see cref="MessageContext.Features"/>.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
public class InboundContext<T> : MessageContext<T>, IEmitContext
{
    private IEmitContext? emitContext;

    /// <inheritdoc/>
    public ITransactionContext? Transaction
    {
        get => GetEmitContext().Transaction;
        set => GetEmitContext().Transaction = value;
    }

    /// <summary>
    /// Lazily resolves the scoped <see cref="IEmitContext"/> from the service provider.
    /// </summary>
    /// <returns>The scoped emit context.</returns>
    private IEmitContext GetEmitContext()
    {
        if (emitContext is null)
        {
            emitContext = Services.GetRequiredService<IEmitContext>();
        }
        return emitContext;
    }
}

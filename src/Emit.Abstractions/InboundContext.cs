namespace Emit.Abstractions;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Base context for all inbound (consumer/handler) pipelines.
/// Each transport provides an internal subclass (e.g., <c>InboundKafkaContext</c>,
/// <c>InboundMediatorContext</c>) that carries transport-specific metadata.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
public abstract class InboundContext<T> : MessageContext<T>, IEmitContext
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

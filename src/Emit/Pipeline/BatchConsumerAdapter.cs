namespace Emit.Pipeline;

using Emit.Abstractions;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Internal adapter that bridges the pipeline's <see cref="IConsumer{T}"/> contract
/// (where T = <see cref="MessageBatch{TValue}"/>) to the user-facing
/// <see cref="IBatchConsumer{TValue}"/> contract.
/// </summary>
internal sealed class BatchConsumerAdapter<TValue, TConcrete> : IConsumer<MessageBatch<TValue>>
    where TConcrete : class, IBatchConsumer<TValue>
{
    /// <inheritdoc />
    public async Task ConsumeAsync(
        ConsumeContext<MessageBatch<TValue>> context,
        CancellationToken cancellationToken)
    {
        var batchConsumer = context.Services.GetRequiredService<TConcrete>();
        await batchConsumer.ConsumeAsync(context, cancellationToken).ConfigureAwait(false);
    }
}

namespace Emit.Pipeline;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Default terminal adapter that resolves a consumer handler from the service provider
/// and invokes it with the consume context.
/// </summary>
/// <typeparam name="TValue">The message value type.</typeparam>
public sealed class HandlerInvoker<TValue>(Type consumerType) : IMiddlewarePipeline<ConsumeContext<TValue>>
{
    /// <inheritdoc />
    public async Task InvokeAsync(ConsumeContext<TValue> context)
    {
        var consumer = (IConsumer<TValue>)context.Services.GetRequiredService(consumerType);
        await consumer.ConsumeAsync(context, context.CancellationToken).ConfigureAwait(false);
    }
}

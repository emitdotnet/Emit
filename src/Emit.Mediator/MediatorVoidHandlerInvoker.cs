namespace Emit.Mediator;

using Emit.Abstractions.Pipeline;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Terminal adapter for void request handlers. Resolves the handler from the scoped
/// service provider and invokes it without producing a response.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
internal sealed class MediatorVoidHandlerInvoker<TRequest>(Type handlerType) : IMiddlewarePipeline<MediatorContext<TRequest>>
    where TRequest : IRequest
{
    /// <inheritdoc />
    public async Task InvokeAsync(MediatorContext<TRequest> context)
    {
        var request = context.Message;
        var handler = (IRequestHandler<TRequest>)context.Services.GetRequiredService(handlerType);

        await handler.HandleAsync(request, context.CancellationToken)
            .ConfigureAwait(false);
    }
}

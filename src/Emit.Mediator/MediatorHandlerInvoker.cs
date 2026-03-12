namespace Emit.Mediator;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Terminal adapter that bridges from the typed pipeline to the mediator handler.
/// Resolves the handler from the scoped service provider, invokes it,
/// and writes the response to the <see cref="IResponseFeature"/>.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
internal sealed class MediatorHandlerInvoker<TRequest, TResponse>(Type handlerType) : IHandlerInvoker<MediatorContext<TRequest>>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc />
    public async Task InvokeAsync(MediatorContext<TRequest> context)
    {
        var request = context.Message;
        var handler = (IRequestHandler<TRequest, TResponse>)context.Services.GetRequiredService(handlerType);

        var response = await handler.HandleAsync(request, context.CancellationToken)
            .ConfigureAwait(false);

        var responseFeature = context.Features.Get<IResponseFeature>()
            ?? throw new InvalidOperationException($"{nameof(IResponseFeature)} not available on this context.");

        await responseFeature.RespondAsync(response).ConfigureAwait(false);
    }
}

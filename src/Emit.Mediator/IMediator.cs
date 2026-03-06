namespace Emit.Mediator;

/// <summary>
/// Dispatches requests to their registered handlers.
/// </summary>
public interface IMediator
{
    /// <summary>
    /// Sends a request to its handler without expecting a response.
    /// </summary>
    /// <param name="request">The request to dispatch.</param>
    /// <param name="cancellationToken">Cancellation token for this operation.</param>
    /// <exception cref="InvalidOperationException">No handler is registered for the request type.</exception>
    Task SendAsync(IRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a request to its handler and returns the response.
    /// </summary>
    /// <typeparam name="TResponse">The response type, inferred from the request.</typeparam>
    /// <param name="request">The request to dispatch.</param>
    /// <param name="cancellationToken">Cancellation token for this operation.</param>
    /// <returns>The response from the handler.</returns>
    /// <exception cref="InvalidOperationException">No handler is registered for the request type.</exception>
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}

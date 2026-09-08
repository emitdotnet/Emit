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

    /// <summary>
    /// Sends a request to its handler and returns the sequence of responses it produces.
    /// </summary>
    /// <typeparam name="TResponse">The response item type, inferred from the request.</typeparam>
    /// <param name="request">The request to dispatch.</param>
    /// <param name="cancellationToken">Cancellation token for this operation.</param>
    /// <returns>The sequence of responses, produced as the handler yields them.</returns>
    /// <exception cref="InvalidOperationException">No handler is registered for the request type.</exception>
    /// <remarks>
    /// Nothing runs until the returned sequence is enumerated. The handler then runs inside the
    /// middleware pipeline for as long as the sequence is being consumed, so middleware wraps
    /// the whole stream rather than just its creation.
    /// <para>
    /// The handler advances only when an item is requested, so it is always between items when
    /// the consumer decides whether to continue. Ending the enumeration early therefore stops
    /// the handler at its next item boundary and completes the request normally, keeping the
    /// work it already did. Cancelling instead aborts the request.
    /// </para>
    /// <para>
    /// Disposing the enumerator completes the request. If the request fails at that point, when
    /// a commit fails for instance, the failure is thrown from the disposal, which for
    /// <c>await foreach</c> means from the loop itself.
    /// </para>
    /// </remarks>
    IAsyncEnumerable<TResponse> CreateStreamAsync<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default);
}

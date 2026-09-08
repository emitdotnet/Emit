namespace Emit.Mediator;

/// <summary>
/// Handles a request that produces a sequence of responses.
/// </summary>
/// <typeparam name="TRequest">The request type. Must implement <see cref="IStreamRequest{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of each item in the produced sequence.</typeparam>
/// <remarks>
/// The returned sequence is enumerated inside the middleware pipeline, so every middleware
/// that applies to a request handler applies here for the whole duration of the stream. The
/// handler runs until the sequence ends or the consumer stops reading.
/// </remarks>
public interface IStreamRequestHandler<in TRequest, out TResponse> : IRequestHandler, IHandlesRequest<TRequest>
    where TRequest : IStreamRequest<TResponse>
{
    /// <summary>
    /// Handles the request and produces its responses as they become available.
    /// </summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="cancellationToken">Cancellation token for this operation.</param>
    /// <returns>The sequence of responses.</returns>
    IAsyncEnumerable<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

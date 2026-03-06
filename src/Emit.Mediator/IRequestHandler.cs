namespace Emit.Mediator;

/// <summary>
/// Non-generic marker interface for request handlers. Used as a compile-time constraint
/// on <see cref="DependencyInjection.MediatorBuilder.AddHandler{THandler}()"/>.
/// </summary>
public interface IRequestHandler;

/// <summary>
/// Marker interface that associates a handler with a specific request type.
/// Provides compile-time type safety for
/// <see cref="DependencyInjection.MediatorBuilder.AddHandler{THandler, TRequest}"/>,
/// ensuring the handler actually handles the specified request type.
/// </summary>
/// <typeparam name="TRequest">The request type this handler processes.</typeparam>
public interface IHandlesRequest<in TRequest>;

/// <summary>
/// Handles a request that does not produce a response.
/// </summary>
/// <typeparam name="TRequest">The request type. Must implement <see cref="IRequest"/>.</typeparam>
public interface IRequestHandler<in TRequest> : IRequestHandler, IHandlesRequest<TRequest>
    where TRequest : IRequest
{
    /// <summary>
    /// Handles the request.
    /// </summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="cancellationToken">Cancellation token for this operation.</param>
    Task HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handles a request and produces a response.
/// </summary>
/// <typeparam name="TRequest">The request type. Must implement <see cref="IRequest{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequestHandler<in TRequest, TResponse> : IRequestHandler, IHandlesRequest<TRequest>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Handles the request and returns a response.
    /// </summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="cancellationToken">Cancellation token for this operation.</param>
    /// <returns>The response produced by the handler.</returns>
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

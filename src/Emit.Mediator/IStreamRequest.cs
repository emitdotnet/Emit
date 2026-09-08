namespace Emit.Mediator;

/// <summary>
/// Marker interface for request types that produce a sequence of responses rather than a
/// single one. Implement this on query objects that stream their results.
/// </summary>
/// <typeparam name="TResponse">The type of each item in the produced sequence.</typeparam>
/// <remarks>
/// A request type implements either <see cref="IRequest"/>, <see cref="IRequest{TResponse}"/>
/// or this interface, never a combination. The distinction determines whether the request is dispatched through
/// <see cref="IMediator.SendAsync{TResponse}"/> or <see cref="IMediator.CreateStreamAsync{TResponse}"/>.
/// </remarks>
public interface IStreamRequest<TResponse>;

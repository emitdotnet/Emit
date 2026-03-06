namespace Emit.Mediator;

/// <summary>
/// Marker interface for request types that do not produce a response.
/// </summary>
public interface IRequest;

/// <summary>
/// Marker interface for request types that self-declare their response type.
/// Implement this on command/query objects to enable type-safe dispatching
/// through the mediator.
/// </summary>
/// <typeparam name="TResponse">The type of the response produced by the handler.</typeparam>
public interface IRequest<TResponse>;

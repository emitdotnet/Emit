namespace Emit.Mediator;

using Emit.Abstractions;

/// <summary>
/// Inbound pipeline context carrying the typed request for the mediator terminal adapter.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
internal sealed class InboundMediatorContext<TRequest> : InboundContext<TRequest>;

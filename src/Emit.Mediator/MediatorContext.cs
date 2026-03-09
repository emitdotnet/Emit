namespace Emit.Mediator;

using Emit.Abstractions;

/// <summary>
/// Context for mediator request/response pipelines. Carries the typed request message
/// and pipeline metadata through the mediator middleware chain.
/// </summary>
/// <typeparam name="T">The request type.</typeparam>
public class MediatorContext<T> : MessageContext<T>;

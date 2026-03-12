namespace Emit.Abstractions.Observability;

/// <summary>
/// Marker interface for Emit observer contracts. Constrains the
/// <c>ObserverInvoker</c> extension method so it does not pollute
/// unrelated collection types.
/// </summary>
public interface IObserver;

namespace Emit.Mediator;

/// <summary>
/// Holds the pre-built dispatch delegates, keyed by request type.
/// Registered as a singleton — the typed pipelines are composed once at container build time.
/// Each dispatcher creates a typed <c>MediatorMessageContext&lt;TRequest&gt;</c> and invokes
/// the typed <c>MessageDelegate&lt;TRequest&gt;</c> pipeline with no runtime reflection.
/// </summary>
internal sealed class MediatorConfiguration(
    IReadOnlyDictionary<Type, Func<object, IServiceProvider, TimeProvider, CancellationToken, MediatorResponseFeature?, Task>> dispatchers)
{
    /// <summary>
    /// Gets the dispatch delegate map keyed by request type.
    /// </summary>
    internal IReadOnlyDictionary<Type, Func<object, IServiceProvider, TimeProvider, CancellationToken, MediatorResponseFeature?, Task>> Dispatchers => dispatchers;
}

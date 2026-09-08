namespace Emit.Mediator;

using Emit.Abstractions;

/// <summary>
/// Runs a request through its composed pipeline.
/// </summary>
/// <param name="request">The request to dispatch.</param>
/// <param name="services">The caller's scope, used to resolve the handler and its dependencies.</param>
/// <param name="timeProvider">Supplies the context timestamp.</param>
/// <param name="cancellationToken">Cancellation token for this operation.</param>
/// <param name="configureFeatures">
/// Seeds the context with whatever the caller needs to get its result back: a response feature
/// for a single response, a sink for a stream, nothing at all for a request without a response.
/// </param>
internal delegate Task MediatorDispatch(
    object request,
    IServiceProvider services,
    TimeProvider timeProvider,
    CancellationToken cancellationToken,
    Action<IFeatureCollection>? configureFeatures);

/// <summary>
/// Holds the pre-built dispatchers, keyed by request type.
/// Registered as a singleton, so the typed pipelines are composed once at container build time.
/// Each dispatcher creates a typed <see cref="MediatorContext{T}"/> and invokes the typed
/// pipeline with no runtime reflection.
/// </summary>
internal sealed class MediatorConfiguration(IReadOnlyDictionary<Type, MediatorDispatch> dispatchers)
{
    /// <summary>
    /// Gets the dispatcher map keyed by request type.
    /// </summary>
    internal IReadOnlyDictionary<Type, MediatorDispatch> Dispatchers => dispatchers;
}

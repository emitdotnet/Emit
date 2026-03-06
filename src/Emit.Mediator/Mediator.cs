namespace Emit.Mediator;
/// <summary>
/// Scoped mediator implementation. Receives the caller's <see cref="IServiceProvider"/>
/// so that handlers share the same DI scope for transactional consistency.
/// </summary>
internal sealed class Mediator(IServiceProvider services, MediatorConfiguration configuration, TimeProvider timeProvider) : IMediator
{
    /// <inheritdoc />
    public async Task SendAsync(IRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dispatcher = GetDispatcher(request.GetType());
        await dispatcher(request, services, timeProvider, cancellationToken, null).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var responseFeature = new MediatorResponseFeature();
        var dispatcher = GetDispatcher(request.GetType());
        await dispatcher(request, services, timeProvider, cancellationToken, responseFeature).ConfigureAwait(false);

        return responseFeature.GetResponse<TResponse>();
    }

    private Func<object, IServiceProvider, TimeProvider, CancellationToken, MediatorResponseFeature?, Task> GetDispatcher(Type requestType)
    {
        if (!configuration.Dispatchers.TryGetValue(requestType, out var dispatcher))
        {
            throw new InvalidOperationException(
                $"No handler is registered for request type '{requestType.Name}'.");
        }

        return dispatcher;
    }
}

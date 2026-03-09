namespace Emit.Routing;

using Emit.Abstractions;

/// <summary>
/// Configures routes for a content-based message router. Each route maps a key value
/// to a consumer handler type, with optional per-route middleware. Unmatched messages
/// always throw <see cref="UnmatchedRouteException"/>, which can be handled via the
/// group-level <c>OnError</c> policy.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="TRouteKey">The route key type (e.g., <see langword="string"/> or an enum).</typeparam>
public sealed class MessageRouterBuilder<TMessage, TRouteKey>
    where TRouteKey : notnull
{
    private readonly List<RouteEntry<TMessage>> routes = [];
    private readonly HashSet<TRouteKey> registeredKeys = [];
    private readonly HashSet<Type> registeredConsumerTypes = [];

    /// <summary>
    /// Registers a route that dispatches to <typeparamref name="TConsumer"/> when the
    /// route selector returns <paramref name="key"/>.
    /// </summary>
    /// <typeparam name="TConsumer">The consumer handler type.</typeparam>
    /// <param name="key">The route key value to match.</param>
    /// <returns>This builder for continued chaining.</returns>
    /// <exception cref="InvalidOperationException">Duplicate route key or consumer type.</exception>
    public MessageRouterBuilder<TMessage, TRouteKey> Route<TConsumer>(TRouteKey key)
        where TConsumer : class, IConsumer<TMessage>
    {
        ValidateRoute<TConsumer>(key);

        routes.Add(new RouteEntry<TMessage>
        {
            RouteKey = key,
            ConsumerType = typeof(TConsumer),
        });

        return this;
    }

    /// <summary>
    /// Registers a route that dispatches to <typeparamref name="TConsumer"/> when the
    /// route selector returns <paramref name="key"/>, with per-route middleware configuration.
    /// </summary>
    /// <typeparam name="TConsumer">The consumer handler type.</typeparam>
    /// <param name="key">The route key value to match.</param>
    /// <param name="configure">Configures per-route middleware and filters.</param>
    /// <returns>This builder for continued chaining.</returns>
    /// <exception cref="InvalidOperationException">Duplicate route key or consumer type.</exception>
    public MessageRouterBuilder<TMessage, TRouteKey> Route<TConsumer>(TRouteKey key, Action<IInboundConfigurable<TMessage>> configure)
        where TConsumer : class, IConsumer<TMessage>
    {
        ArgumentNullException.ThrowIfNull(configure);
        ValidateRoute<TConsumer>(key);

        var configurator = new RouteHandlerConfigurator<TMessage>();
        configure(configurator);

        routes.Add(new RouteEntry<TMessage>
        {
            RouteKey = key,
            ConsumerType = typeof(TConsumer),
            Pipeline = configurator.Pipeline.Descriptors.Count > 0 ? configurator.Pipeline : null,
        });

        return this;
    }

    /// <summary>
    /// Builds the router registration from the configured routes and selector.
    /// Called by provider-specific consumer group builders (e.g., Kafka).
    /// </summary>
    /// <param name="selector">The route selector function.</param>
    /// <returns>An immutable registration descriptor.</returns>
    /// <exception cref="InvalidOperationException">No routes were registered.</exception>
    public RouterRegistration<TMessage> Build(Func<ConsumeContext<TMessage>, TRouteKey?> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        if (routes.Count == 0)
        {
            throw new InvalidOperationException(
                "At least one route must be registered on the router.");
        }

        return new RouterRegistration<TMessage>(
            context => selector(context),
            routes.ToList());
    }

    private void ValidateRoute<TConsumer>(TRouteKey key)
    {
        if (!registeredKeys.Add(key))
        {
            throw new InvalidOperationException(
                $"Route key '{key}' has already been registered in this router.");
        }

        var consumerType = typeof(TConsumer);
        if (!registeredConsumerTypes.Add(consumerType))
        {
            throw new InvalidOperationException(
                $"Consumer type '{consumerType.Name}' has already been registered in this router.");
        }
    }
}

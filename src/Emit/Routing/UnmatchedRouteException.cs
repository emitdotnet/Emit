namespace Emit.Routing;

/// <summary>
/// Thrown when a message router cannot find a matching route for the incoming message
/// and the unmatched behavior is configured to throw. Users can match on this exception
/// type in their <c>OnError</c> policy to dead-letter unroutable messages.
/// </summary>
public sealed class UnmatchedRouteException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnmatchedRouteException"/> class.
    /// </summary>
    /// <param name="routeKey">The route key that did not match any registered route, or <c>null</c> if the selector returned <c>null</c>.</param>
    public UnmatchedRouteException(object? routeKey)
        : base(routeKey is not null
            ? $"No route matched for key '{routeKey}'."
            : "Route selector returned null (no route key).")
    {
        RouteKey = routeKey;
    }

    /// <summary>
    /// The route key that did not match any registered route, or <c>null</c> if the selector returned <c>null</c>.
    /// </summary>
    public object? RouteKey { get; }
}

namespace Emit.Abstractions.Pipeline;

/// <summary>
/// Controls when a middleware instance is created and how long it lives.
/// </summary>
public enum MiddlewareLifetime
{
    /// <summary>
    /// The middleware is resolved once when the pipeline is built and reused for every message.
    /// For type-based middleware, the instance is resolved from the root service provider.
    /// For factory-based middleware, the factory is invoked once at build time.
    /// This is the default lifetime.
    /// </summary>
    Singleton = 0,

    /// <summary>
    /// The middleware is resolved per message from the scoped service provider
    /// available via <see cref="MessageContext.Services"/>.
    /// For type-based middleware, the type must be registered as a scoped service.
    /// For factory-based middleware, the factory is invoked per message with the scoped provider.
    /// </summary>
    Scoped = 1,
}

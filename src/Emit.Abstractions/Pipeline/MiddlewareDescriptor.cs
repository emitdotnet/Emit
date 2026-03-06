namespace Emit.Abstractions.Pipeline;

/// <summary>
/// Describes a middleware registration: either a type or a factory, combined with a lifetime.
/// For type-based registrations, the type may be an open generic
/// (e.g., <c>typeof(LoggingMiddleware&lt;&gt;)</c>) that is closed per message type at pipeline build time.
/// </summary>
public sealed class MiddlewareDescriptor
{
    /// <summary>
    /// The concrete (or open generic) middleware type for type-based registrations,
    /// or <c>null</c> for factory-based.
    /// </summary>
    public Type? MiddlewareType { get; }

    /// <summary>
    /// The factory delegate for factory-based registrations, or <c>null</c> for type-based.
    /// Stored as <see cref="Delegate"/> to support typed factories
    /// (<c>Func&lt;IServiceProvider, IMiddleware&lt;TContext&gt;&gt;</c>), cast at build time.
    /// </summary>
    public Delegate? Factory { get; }

    /// <summary>
    /// The lifetime controlling when the middleware instance is created.
    /// </summary>
    public MiddlewareLifetime Lifetime { get; }

    private MiddlewareDescriptor(Type? middlewareType, Delegate? factory, MiddlewareLifetime lifetime)
    {
        MiddlewareType = middlewareType;
        Factory = factory;
        Lifetime = lifetime;
    }

    /// <summary>
    /// Creates a type-based middleware descriptor from a runtime <see cref="Type"/>.
    /// The type must implement <see cref="IMiddleware{TContext}"/> (closed) or be an open generic
    /// whose closed form implements it. Validation of closed types against a specific context type
    /// happens at pipeline build time.
    /// </summary>
    /// <param name="middlewareType">
    /// The middleware type. May be an open generic type definition
    /// (e.g., <c>typeof(LoggingMiddleware&lt;&gt;)</c>) or a closed type that implements
    /// <see cref="IMiddleware{TContext}"/>.
    /// </param>
    /// <param name="lifetime">
    /// The middleware lifetime. Defaults to <see cref="MiddlewareLifetime.Singleton"/>.
    /// </param>
    public static MiddlewareDescriptor ForType(Type middlewareType, MiddlewareLifetime lifetime = default)
    {
        ArgumentNullException.ThrowIfNull(middlewareType);

        if (!ImplementsMiddleware(middlewareType))
        {
            throw new ArgumentException(
                $"Type '{middlewareType.Name}' does not implement IMiddleware<TContext>.",
                nameof(middlewareType));
        }

        return new(middlewareType, factory: null, lifetime);
    }

    /// <summary>
    /// Creates a factory-based middleware descriptor for a specific context type.
    /// </summary>
    /// <typeparam name="TContext">The pipeline context type.</typeparam>
    /// <param name="factory">
    /// A factory that receives an <see cref="IServiceProvider"/> and returns a typed middleware instance.
    /// For <see cref="MiddlewareLifetime.Singleton"/>, invoked once at build time with the root provider.
    /// For <see cref="MiddlewareLifetime.Scoped"/>, invoked per message with the scoped provider.
    /// </param>
    /// <param name="lifetime">
    /// The middleware lifetime. Defaults to <see cref="MiddlewareLifetime.Singleton"/>.
    /// </param>
    public static MiddlewareDescriptor ForFactory<TContext>(
        Func<IServiceProvider, IMiddleware<TContext>> factory,
        MiddlewareLifetime lifetime = default)
        where TContext : MessageContext
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new(middlewareType: null, factory, lifetime);
    }

    private static bool ImplementsMiddleware(Type type)
    {
        return type.GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMiddleware<>));
    }
}

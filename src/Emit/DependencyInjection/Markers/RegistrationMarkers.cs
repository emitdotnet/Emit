namespace Emit.DependencyInjection;

/// <summary>
/// Marker registered by persistence providers to signal their presence for validation.
/// </summary>
public sealed record PersistenceProviderMarker(string ProviderName);

/// <summary>
/// Marker registered by outbox providers to signal their presence for validation.
/// </summary>
public sealed class OutboxProviderMarker;

/// <summary>
/// Marker registered by a persistence provider when the outbox is enabled.
/// </summary>
public sealed record OutboxRegistrationMarker(string ProviderName);

/// <summary>
/// Marker registered by a persistence provider when the distributed lock is enabled.
/// </summary>
public sealed record DistributedLockRegistrationMarker(string ProviderName);

/// <summary>
/// Marker registered for each handler type decorated with <c>[Transactional]</c>, so the
/// configuration can be validated once every integration has been registered.
/// </summary>
/// <param name="HandlerType">The decorated consumer or mediator handler type.</param>
public sealed record TransactionalHandlerMarker(Type HandlerType);

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

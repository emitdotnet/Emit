namespace BuildingSentinel.Common.Repositories;

using BuildingSentinel.Common.Domain;

/// <summary>
/// Stores raw building events for audit and replay.
/// </summary>
public interface IBuildingEventRepository
{
    /// <summary>Persists <paramref name="evt"/> to the underlying store within the caller's active transaction.</summary>
    Task SaveAsync(BuildingEvent evt, CancellationToken cancellationToken = default);
}

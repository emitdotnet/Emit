namespace BuildingSentinel.Common.Repositories;

using BuildingSentinel.Common.Domain;

/// <summary>
/// Stores raw building events for audit and replay.
/// </summary>
public interface IBuildingEventRepository
{
    /// <summary>Inserts <paramref name="evt"/> into the underlying store within the caller's active transaction.</summary>
    Task InsertAsync(BuildingEvent evt, CancellationToken cancellationToken = default);
}

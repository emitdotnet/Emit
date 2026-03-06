namespace InventorySnapshot.Common.Repositories;

using InventorySnapshot.Common.Domain;

/// <summary>Persistence for inventory snapshots.</summary>
public interface ISnapshotRepository
{
    /// <summary>Persists a new snapshot record.</summary>
    Task SaveAsync(StockSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>Returns the total number of snapshots taken so far.</summary>
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);
}

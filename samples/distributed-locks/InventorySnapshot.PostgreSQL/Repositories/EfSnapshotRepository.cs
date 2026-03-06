namespace InventorySnapshot.PostgreSQL.Repositories;

using InventorySnapshot.Common.Domain;
using InventorySnapshot.Common.Repositories;
using InventorySnapshot.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;

internal sealed class EfSnapshotRepository(IDbContextFactory<SampleDbContext> factory) : ISnapshotRepository
{
    public async Task SaveAsync(StockSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        db.Snapshots.Add(new SnapshotEntity
        {
            Id            = snapshot.Id,
            TakenAt       = snapshot.TakenAt,
            TotalProducts = snapshot.TotalProducts,
            TotalUnits    = snapshot.TotalUnits,
            WarehouseCount = snapshot.WarehouseCount,
            DurationMs    = snapshot.Duration.TotalMilliseconds,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Snapshots.CountAsync(cancellationToken).ConfigureAwait(false);
    }
}

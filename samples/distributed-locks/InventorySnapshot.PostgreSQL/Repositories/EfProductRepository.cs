namespace InventorySnapshot.PostgreSQL.Repositories;

using InventorySnapshot.Common.Domain;
using InventorySnapshot.Common.Repositories;
using InventorySnapshot.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;

internal sealed class EfProductRepository(IDbContextFactory<SampleDbContext> factory) : IProductRepository
{
    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var entities = await db.Products
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(e => new Product
        {
            Sku         = e.Sku,
            Name        = e.Name,
            WarehouseId = e.WarehouseId,
            Stock       = e.Stock,
        }).ToList();
    }

    public async Task AdjustStockAsync(string sku, int delta, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        await db.Products
            .Where(p => p.Sku == sku)
            .ExecuteUpdateAsync(
                s => s.SetProperty(p => p.Stock, p => Math.Max(0, p.Stock + delta)),
                cancellationToken)
            .ConfigureAwait(false);
    }
}

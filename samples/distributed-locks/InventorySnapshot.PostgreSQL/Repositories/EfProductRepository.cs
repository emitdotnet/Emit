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

        await db.Database
            .ExecuteSqlInterpolatedAsync(
                $"UPDATE products SET stock = GREATEST(0, stock + {delta}) WHERE sku = {sku}",
                cancellationToken)
            .ConfigureAwait(false);
    }
}

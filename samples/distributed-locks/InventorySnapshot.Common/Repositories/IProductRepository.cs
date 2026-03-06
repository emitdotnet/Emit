namespace InventorySnapshot.Common.Repositories;

using InventorySnapshot.Common.Domain;

/// <summary>Read/write access to warehouse product records.</summary>
public interface IProductRepository
{
    /// <summary>Returns all products across all warehouses.</summary>
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Applies a stock delta (positive = delivery, negative = shipment) to a single SKU.</summary>
    Task AdjustStockAsync(string sku, int delta, CancellationToken cancellationToken = default);
}

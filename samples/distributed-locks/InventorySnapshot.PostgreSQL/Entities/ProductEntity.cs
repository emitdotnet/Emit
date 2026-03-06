namespace InventorySnapshot.PostgreSQL.Entities;

public sealed class ProductEntity
{
    public string Sku { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string WarehouseId { get; set; } = default!;
    public int Stock { get; set; }
}

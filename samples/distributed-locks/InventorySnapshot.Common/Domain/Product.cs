namespace InventorySnapshot.Common.Domain;

/// <summary>A product tracked in the warehouse inventory.</summary>
public sealed record Product
{
    /// <summary>Stock-keeping unit — the unique product identifier.</summary>
    public string Sku { get; init; } = default!;

    /// <summary>Human-readable product name.</summary>
    public string Name { get; init; } = default!;

    /// <summary>The warehouse location where stock is held.</summary>
    public string WarehouseId { get; init; } = default!;

    /// <summary>Current unit count on hand.</summary>
    public int Stock { get; init; }
}

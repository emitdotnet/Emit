namespace InventorySnapshot.Common.Domain;

/// <summary>A point-in-time summary of all warehouse inventory.</summary>
public sealed record StockSnapshot
{
    /// <summary>Unique identifier for this snapshot run.</summary>
    public Guid Id { get; init; }

    /// <summary>UTC timestamp when the snapshot was taken.</summary>
    public DateTime TakenAt { get; init; }

    /// <summary>Number of distinct product SKUs across all warehouses.</summary>
    public int TotalProducts { get; init; }

    /// <summary>Sum of all unit stocks across all products and warehouses.</summary>
    public int TotalUnits { get; init; }

    /// <summary>Number of distinct warehouses represented.</summary>
    public int WarehouseCount { get; init; }

    /// <summary>Time taken to compute and persist the snapshot.</summary>
    public TimeSpan Duration { get; init; }
}

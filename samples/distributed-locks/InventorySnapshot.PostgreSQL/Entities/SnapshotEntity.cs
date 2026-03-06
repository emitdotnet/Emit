namespace InventorySnapshot.PostgreSQL.Entities;

public sealed class SnapshotEntity
{
    public Guid Id { get; set; }
    public DateTime TakenAt { get; set; }
    public int TotalProducts { get; set; }
    public int TotalUnits { get; set; }
    public int WarehouseCount { get; set; }
    public double DurationMs { get; set; }
}

namespace InventorySnapshot.PostgreSQL;

using Emit.EntityFrameworkCore.DependencyInjection;
using InventorySnapshot.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;

public sealed class SampleDbContext(DbContextOptions<SampleDbContext> options) : DbContext(options)
{
    public DbSet<ProductEntity> Products { get; set; } = default!;
    public DbSet<SnapshotEntity> Snapshots { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Register Emit's lock table schema (required for UseDistributedLock)
        modelBuilder.AddEmitModel(emit => emit.UseNpgsql());

        modelBuilder.Entity<ProductEntity>(e =>
        {
            e.ToTable("products");
            e.HasKey(x => x.Sku);
            e.Property(x => x.Sku).IsRequired().HasMaxLength(64);
            e.Property(x => x.Name).IsRequired().HasMaxLength(256);
            e.Property(x => x.WarehouseId).IsRequired().HasMaxLength(64);
            e.Property(x => x.Stock).IsRequired();
        });

        modelBuilder.Entity<SnapshotEntity>(e =>
        {
            e.ToTable("snapshots");
            e.HasKey(x => x.Id);
            e.Property(x => x.TakenAt).IsRequired();
            e.Property(x => x.TotalProducts).IsRequired();
            e.Property(x => x.TotalUnits).IsRequired();
            e.Property(x => x.WarehouseCount).IsRequired();
            e.Property(x => x.DurationMs).IsRequired();
        });
    }
}

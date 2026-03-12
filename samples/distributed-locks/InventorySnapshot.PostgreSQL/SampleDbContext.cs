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
            e.HasKey(x => x.Sku);
            e.Property(x => x.Sku).IsRequired().HasMaxLength(64);
            e.Property(x => x.Name).IsRequired().HasMaxLength(256);
            e.Property(x => x.WarehouseId).IsRequired().HasMaxLength(64);
        });

        modelBuilder.Entity<SnapshotEntity>(e =>
        {
            e.HasKey(x => x.Id);
        });
    }
}

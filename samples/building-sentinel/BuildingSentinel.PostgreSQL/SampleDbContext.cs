namespace BuildingSentinel.PostgreSQL;

using BuildingSentinel.PostgreSQL.Entities;
using Emit.EntityFrameworkCore.DependencyInjection;
using Microsoft.EntityFrameworkCore;

public class SampleDbContext(DbContextOptions<SampleDbContext> options) : DbContext(options)
{
    public DbSet<BuildingEventEntity> BuildingEvents { get; set; } = default!;
    public DbSet<AccessDenialAlertEntity> AccessDenialAlerts { get; set; } = default!;
    public DbSet<DeviceHeartbeatEntity> DeviceHeartbeats { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddEmitModel(emit => emit.UseNpgsql());

        modelBuilder.Entity<BuildingEventEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityByDefaultColumn();
            e.Property(x => x.DeviceId).IsRequired().HasMaxLength(128);
            e.Property(x => x.EventType).IsRequired().HasMaxLength(64);
            e.Property(x => x.Location).IsRequired().HasMaxLength(256);
        });

        modelBuilder.Entity<AccessDenialAlertEntity>(e =>
        {
            e.HasKey(x => x.BadgeId);
            e.Property(x => x.BadgeId).IsRequired().HasMaxLength(128);
        });

        modelBuilder.Entity<DeviceHeartbeatEntity>(e =>
        {
            e.HasKey(x => x.DeviceId);
            e.Property(x => x.DeviceId).IsRequired().HasMaxLength(128);
        });
    }
}

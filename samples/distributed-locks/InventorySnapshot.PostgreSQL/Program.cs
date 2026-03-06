using Emit.DependencyInjection;
using Emit.OpenTelemetry;
using Emit.EntityFrameworkCore.DependencyInjection;
using Emit.EntityFrameworkCore.HealthChecks;
using InventorySnapshot.Common.Repositories;
using InventorySnapshot.Common.Simulation;
using InventorySnapshot.Common.Workers;
using InventorySnapshot.PostgreSQL;
using InventorySnapshot.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// ── OpenTelemetry ─────────────────────────────────────────────────────────────
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("InventorySnapshot"))
    .WithMetrics(metrics => metrics
        .AddEmitInstrumentation()
        .AddPrometheusExporter())
    .WithTracing(tracing => tracing
        .AddEmitInstrumentation()
        .AddOtlpExporter(options =>
            options.Endpoint = new Uri(builder.Configuration["Otlp:Endpoint"]!)));

// ── EF Core ───────────────────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;

builder.Services.AddDbContext<SampleDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddDbContextFactory<SampleDbContext>(options =>
    options.UseNpgsql(connectionString));

// ── Application services ───────────────────────────────────────────────────────
// Singleton is correct: EfProductRepository and EfSnapshotRepository use IDbContextFactory,
// which creates a fresh DbContext per call. They hold no mutable state and are safe to share.
builder.Services.AddSingleton<IProductRepository, EfProductRepository>();
builder.Services.AddSingleton<ISnapshotRepository, EfSnapshotRepository>();
builder.Services.AddHostedService<StockSimulatorService>();
builder.Services.AddHostedService<InventorySnapshotWorker>();

builder.Services.Configure<SnapshotWorkerOptions>(
    builder.Configuration.GetSection("Snapshot"));

// ── Emit ──────────────────────────────────────────────────────────────────────
builder.Services.AddEmit(emit =>
{
    emit.AddEntityFrameworkCore<SampleDbContext>(ef =>
    {
        ef.UseNpgsql();
        ef.UseDistributedLock();
    });
});

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddEmitPostgreSQL<SampleDbContext>();

var app = builder.Build();

// ── Database initialisation ───────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
    await db.Database.EnsureCreatedAsync();

    if (!db.Products.Any())
    {
        db.Products.AddRange(
            new() { Sku = "WRENCH-12",  Name = "12mm Socket Wrench",  WarehouseId = "WH-NORTH", Stock = 250  },
            new() { Sku = "BOLT-M8",    Name = "M8 Hex Bolt (pack)",  WarehouseId = "WH-NORTH", Stock = 1500 },
            new() { Sku = "CABLE-CAT6", Name = "CAT6 Cable 5m",       WarehouseId = "WH-SOUTH", Stock = 340  },
            new() { Sku = "GLOVE-L",    Name = "Work Gloves (L)",      WarehouseId = "WH-EAST",  Stock = 85   },
            new() { Sku = "HELMET-XL",  Name = "Safety Helmet (XL)",   WarehouseId = "WH-EAST",  Stock = 42   },
            new() { Sku = "PUMP-2HP",   Name = "2HP Water Pump",       WarehouseId = "WH-SOUTH", Stock = 18   },
            new() { Sku = "TAPE-50M",   Name = "Electrical Tape 50m",  WarehouseId = "WH-NORTH", Stock = 620  },
            new() { Sku = "DRILL-BIT",  Name = "HSS Drill Bit Set",    WarehouseId = "WH-WEST",  Stock = 95   },
            new() { Sku = "VALVE-1IN",  Name = "Ball Valve 1-inch",    WarehouseId = "WH-WEST",  Stock = 160  },
            new() { Sku = "FILTER-OIL", Name = "Oil Filter (generic)", WarehouseId = "WH-EAST",  Stock = 430  }
        );
        await db.SaveChangesAsync();
    }
}

// ── Middleware ────────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");
app.UseOpenTelemetryPrometheusScrapingEndpoint();

app.Run();

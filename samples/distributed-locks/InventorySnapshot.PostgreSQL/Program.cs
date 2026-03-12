using Emit.DependencyInjection;
using Emit.OpenTelemetry;
using Emit.EntityFrameworkCore.DependencyInjection;
using Emit.EntityFrameworkCore.HealthChecks;
using InventorySnapshot.Common.Domain;
using InventorySnapshot.Common.Repositories;
using InventorySnapshot.Common.Simulation;
using InventorySnapshot.Common.Workers;
using InventorySnapshot.PostgreSQL;
using InventorySnapshot.PostgreSQL.Entities;
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
    options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

builder.Services.AddDbContextFactory<SampleDbContext>(options =>
    options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

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
        db.Products.AddRange(ProductCatalog.Seeds.Select(p =>
            new ProductEntity { Sku = p.Sku, Name = p.Name, WarehouseId = p.WarehouseId, Stock = p.Stock }));
        await db.SaveChangesAsync();
    }
}

// ── Middleware ────────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");
app.UseOpenTelemetryPrometheusScrapingEndpoint();

app.Run();

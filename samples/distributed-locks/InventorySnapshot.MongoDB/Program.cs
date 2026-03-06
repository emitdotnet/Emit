using Emit.DependencyInjection;
using Emit.OpenTelemetry;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.HealthChecks;
using InventorySnapshot.Common.Repositories;
using InventorySnapshot.Common.Simulation;
using InventorySnapshot.Common.Workers;
using InventorySnapshot.MongoDB.Repositories;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using global::MongoDB.Driver;

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

// ── MongoDB ───────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IMongoClient>(_ =>
    new MongoClient(builder.Configuration.GetConnectionString("MongoDB")));

builder.Services.AddSingleton<IMongoDatabase>(sp =>
    sp.GetRequiredService<IMongoClient>().GetDatabase("inventory_snapshot"));

// ── Application services ───────────────────────────────────────────────────────
builder.Services.AddSingleton<IProductRepository, MongoProductRepository>();
builder.Services.AddSingleton<ISnapshotRepository, MongoSnapshotRepository>();
builder.Services.AddHostedService<StockSimulatorService>();
builder.Services.AddHostedService<InventorySnapshotWorker>();

builder.Services.Configure<SnapshotWorkerOptions>(
    builder.Configuration.GetSection("Snapshot"));

// ── Emit ──────────────────────────────────────────────────────────────────────
builder.Services.AddEmit(emit =>
{
    emit.AddMongoDb(mongo =>
    {
        mongo.Configure((sp, ctx) =>
        {
            ctx.Client = sp.GetRequiredService<IMongoClient>();
            ctx.Database = sp.GetRequiredService<IMongoDatabase>();
        });
        mongo.UseDistributedLock();
    });
});

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddEmitMongoDB();

var app = builder.Build();

// ── Middleware ────────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");
app.UseOpenTelemetryPrometheusScrapingEndpoint();

app.Run();

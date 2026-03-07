using BuildingSentinel.Common.Endpoints;
using BuildingSentinel.Common.Extensions;
using BuildingSentinel.Common.Repositories;
using BuildingSentinel.Common.Transactions;
using BuildingSentinel.PostgreSQL;
using BuildingSentinel.PostgreSQL.Repositories;
using BuildingSentinel.Common.Simulation;
using BuildingSentinel.PostgreSQL.Transactions;
using Emit.DependencyInjection;
using Emit.Mediator.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Emit.OpenTelemetry;
using Emit.EntityFrameworkCore.DependencyInjection;
using Emit.EntityFrameworkCore.HealthChecks;
using Emit.Kafka.HealthChecks;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── OpenAPI ──────────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();
builder.Services.AddHttpClient();

// ── OpenTelemetry ─────────────────────────────────────────────────────────────
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("BuildingSentinel"))
    .WithMetrics(metrics => metrics
        .AddEmitInstrumentation()
        .AddAspNetCoreInstrumentation()
        .AddPrometheusExporter())
    .WithTracing(tracing => tracing
        .AddEmitInstrumentation()
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(options =>
            options.Endpoint = new Uri(builder.Configuration["Otlp:Endpoint"]!)));

// ── EF Core ───────────────────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;

builder.Services.AddDbContext<SampleDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddDbContextFactory<SampleDbContext>(options =>
    options.UseNpgsql(connectionString));

// ── Application repositories ──────────────────────────────────────────────────
builder.Services.AddScoped<IBuildingEventRepository, EfBuildingEventRepository>();
builder.Services.AddScoped<IAlertRepository, EfAlertRepository>();
builder.Services.AddScoped<IDeviceHeartbeatRepository, EfDeviceHeartbeatRepository>();
builder.Services.AddScoped<ITransactionFactory, EfTransactionFactory>();
builder.Services.AddHostedService<BuildingSimulatorService>();

// ── Emit ──────────────────────────────────────────────────────────────────────
builder.Services.AddEmit(emit =>
{
    emit.AddEntityFrameworkCore<SampleDbContext>(ef =>
    {
        ef.UseNpgsql();
        ef.UseOutbox();
        ef.UseDistributedLock();
    });

    emit.AddKafka(kafka =>
    {
        kafka.ConfigureClient(config =>
            config.BootstrapServers = builder.Configuration["Kafka:BootstrapServers"]);

        kafka.ConfigureSchemaRegistry(sr =>
            sr.Url = builder.Configuration["SchemaRegistry:Url"]);

        kafka.AddBuildingSentinelTopics();
    });

    emit.AddMediator(m => m.AddBuildingSentinelHandlers());
});

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddEmitKafka()
    .AddEmitPostgreSQL<SampleDbContext>();

var app = builder.Build();

// ── Database initialisation ───────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// ── Middleware ────────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");
app.MapOpenApi();
app.MapScalarApiReference();
app.UseOpenTelemetryPrometheusScrapingEndpoint();
app.MapBuildingEventEndpoints();

app.Run();

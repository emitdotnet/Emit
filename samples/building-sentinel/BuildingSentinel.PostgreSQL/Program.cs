using BuildingSentinel.Common.Daemons;
using BuildingSentinel.Common.Endpoints;
using BuildingSentinel.Common.Extensions;
using BuildingSentinel.Common.Repositories;
using BuildingSentinel.PostgreSQL;
using BuildingSentinel.PostgreSQL.Repositories;
using BuildingSentinel.Common.Simulation;
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
using BuildingSentinel.Common.Validation;
using FluentValidation;
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
    options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

builder.Services.AddDbContextFactory<SampleDbContext>(options =>
    options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

// ── Application repositories ──────────────────────────────────────────────────
builder.Services.AddScoped<IBuildingEventRepository, EfBuildingEventRepository>();
builder.Services.AddScoped<IAlertRepository, EfAlertRepository>();
builder.Services.AddScoped<IDeviceHeartbeatRepository, EfDeviceHeartbeatRepository>();
builder.Services.AddScoped<IDeviceAlertRepository, EfDeviceAlertRepository>();
builder.Services.AddHostedService<BuildingSimulatorService>();
builder.Services.AddValidatorsFromAssemblyContaining<BuildingEventValidator>();

// ── Emit ──────────────────────────────────────────────────────────────────────
builder.Services.AddEmit(emit =>
{
    // Every replica of this application shares one service name, which scopes leader election, daemons and the
    // outbox. It defaults to the application name; setting it keeps it stable if the project is renamed, since
    // outbox entries are only delivered by the service that enqueued them.
    emit.ConfigureLeaderElection(options => options.ServiceName = "building-sentinel");

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

    // Runs on exactly one instance at a time, and moves to another when that instance goes away
    emit.AddDaemon<DeviceOfflineDaemon>();
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

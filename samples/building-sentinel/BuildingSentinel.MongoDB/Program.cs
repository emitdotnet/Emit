using BuildingSentinel.Common.Endpoints;
using BuildingSentinel.Common.Extensions;
using BuildingSentinel.Common.Repositories;
using BuildingSentinel.Common.Transactions;
using BuildingSentinel.MongoDB.Repositories;
using BuildingSentinel.Common.Simulation;
using BuildingSentinel.MongoDB.Transactions;
using Emit.DependencyInjection;
using Emit.Mediator.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Emit.OpenTelemetry;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.HealthChecks;
using Emit.Kafka.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using global::MongoDB.Driver;

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

// ── MongoDB ───────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IMongoClient>(_ =>
    new MongoClient(builder.Configuration.GetConnectionString("MongoDB")));

builder.Services.AddSingleton<IMongoDatabase>(sp =>
    sp.GetRequiredService<IMongoClient>().GetDatabase("building_sentinel"));

// ── Application repositories ──────────────────────────────────────────────────
builder.Services.AddScoped<IBuildingEventRepository, MongoBuildingEventRepository>();
builder.Services.AddScoped<IAlertRepository, MongoAlertRepository>();
builder.Services.AddScoped<IDeviceHeartbeatRepository, MongoDeviceHeartbeatRepository>();
builder.Services.AddScoped<ITransactionFactory, MongoTransactionFactory>();
builder.Services.AddHostedService<BuildingSimulatorService>();

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
        mongo.UseOutbox();
        mongo.UseDistributedLock();
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
    .AddEmitMongoDB();

var app = builder.Build();

// ── Middleware ────────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");
app.MapOpenApi();
app.MapScalarApiReference();
app.UseOpenTelemetryPrometheusScrapingEndpoint();
app.MapBuildingEventEndpoints();

app.Run();

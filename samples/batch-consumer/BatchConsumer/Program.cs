using BatchConsumer.Extensions;
using BatchConsumer.Repositories;
using BatchConsumer.Simulation;
using BatchConsumer.Validation;
using Emit.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.HealthChecks;
using Emit.MongoDB.DependencyInjection;
using Emit.MongoDB.HealthChecks;
using Emit.OpenTelemetry;
using FluentValidation;
using global::MongoDB.Driver;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// ── OpenTelemetry ─────────────────────────────────────────────────────────────
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("BatchConsumer"))
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
    sp.GetRequiredService<IMongoClient>().GetDatabase("batch_consumer"));

// ── Application services ──────────────────────────────────────────────────────
builder.Services.AddScoped<IPackageJourneyRepository, MongoPackageJourneyRepository>();
builder.Services.AddHostedService<ConveyorSimulatorService>();
builder.Services.AddValidatorsFromAssemblyContaining<PackageScanValidator>();

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
    });

    emit.AddKafka(kafka =>
    {
        kafka.ConfigureClient(config =>
            config.BootstrapServers = builder.Configuration["Kafka:BootstrapServers"]);

        kafka.ConfigureSchemaRegistry(sr =>
            sr.Url = builder.Configuration["SchemaRegistry:Url"]);

        kafka.AddPackageSorterTopics();
    });
});

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddEmitKafka()
    .AddEmitMongoDB();

var app = builder.Build();

// ── Middleware ─────────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");
app.UseOpenTelemetryPrometheusScrapingEndpoint();

app.Run();

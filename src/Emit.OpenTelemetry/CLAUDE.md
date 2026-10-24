# Emit.OpenTelemetry/

OpenTelemetry integration for Emit metrics and tracing.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `README.md` | Package description and usage overview | Understand what this package provides |
| `Emit.OpenTelemetry.csproj` | OpenTelemetry integration project file with OpenTelemetry.Api dependency | Configure OpenTelemetry integration dependencies |
| `EmitInstrumentationOptions.cs` | Which of Emit's meters to subscribe to, package meters to skip with DisableMeter, and enrichment tags | Configure metric collection behavior or customize instrumentation options |
| `EmitOpenTelemetryExtensions.cs` | AddEmitInstrumentation extension on TracerProviderBuilder registering Emit's ActivitySources | Register Emit distributed tracing with OpenTelemetry |
| `MeterProviderBuilderExtensions.cs` | AddEmitInstrumentation: subscribes to Emit's own meters and to the package meters registered in the service collection, and adds the enrichment tags | Register Emit metrics with OpenTelemetry MeterProvider |

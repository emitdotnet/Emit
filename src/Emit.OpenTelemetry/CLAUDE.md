# Emit.OpenTelemetry/

OpenTelemetry integration for Emit metrics and tracing.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `README.md` | Package description and usage overview | Understand what this package provides |
| `Emit.OpenTelemetry.csproj` | OpenTelemetry integration project file with OpenTelemetry.Api dependency | Configure OpenTelemetry integration dependencies |
| `EmitInstrumentationOptions.cs` | Configuration options for Emit OpenTelemetry instrumentation | Configure metric collection behavior or customize instrumentation options |
| `EmitOpenTelemetryExtensions.cs` | AddEmitInstrumentation extension on TracerProviderBuilder registering Emit's ActivitySources | Register Emit distributed tracing with OpenTelemetry |
| `MeterProviderBuilderExtensions.cs` | AddEmitInstrumentation extension method registering Emit meters with OpenTelemetry | Register Emit metrics with OpenTelemetry MeterProvider |

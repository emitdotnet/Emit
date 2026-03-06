# Tracing/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `EmitActivitySourceNames.cs` | Constants for OpenTelemetry ActivitySource names used across the Emit library | Reference activity source names for distributed tracing configuration |
| `EnrichmentContext.cs` | Context passed to activity enrichers containing message and pipeline metadata | Implement custom activity enrichment or understand enrichment data model |
| `IActivityEnricher.cs` | Interface for enriching OpenTelemetry activities with custom tags and metadata | Implement custom activity enrichment for distributed tracing |

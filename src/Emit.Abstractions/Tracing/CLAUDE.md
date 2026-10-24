# Tracing/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `EmitActivitySourceNames.cs` | Names of Emit's ActivitySources: `Emit.Producer`, `Emit.Consumer`, `Emit.Mediator` | Reference activity source names for distributed tracing configuration |
| `EnrichmentPhases.cs` | The values of `EnrichmentContext.Phase`: produce, consume, mediator | Check which span an enricher is called for |
| `EnrichmentContext.cs` | Context passed to activity enrichers containing message and pipeline metadata | Implement custom activity enrichment or understand enrichment data model |
| `IActivityEnricher.cs` | Interface for enriching OpenTelemetry activities with custom tags and metadata | Implement custom activity enrichment for distributed tracing |

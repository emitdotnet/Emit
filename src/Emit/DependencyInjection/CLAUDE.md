# DependencyInjection/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `EmitBuilder.cs` | Fluent builder for persistence, providers, policies, and options with XOR persistence enforcement | Extend registration API or understand builder validation |
| `EmitBuilderObserverExtensions.cs` | Observer registration extensions for consume, produce, and outbox lifecycle hooks | Register observers for monitoring or instrumentation |
| `EmitTracingBuilder.cs` | Fluent builder for configuring distributed tracing and activity enrichment | Configure tracing options or register custom activity enrichers |
| `ServiceCollectionExtensions.cs` | AddEmit entry point registering options, validators, workers, and policy registry | Understand DI registration flow or add new top-level services |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Markers/` | Sentinel records signaling persistence and outbox provider registration presence | Implement provider validation or understand how providers signal their presence |

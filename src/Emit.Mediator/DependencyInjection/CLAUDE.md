# DependencyInjection/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `MediatorEmitBuilderExtensions.cs` | AddMediator extension method registering mediator services and building typed dispatch pipelines: built-in tracing, metrics and observers around the level of every pipeline, the mediator level, the unit of work and the handler's own middleware | Register mediator or understand DI registration flow |
| `MediatorBuilder.cs` | Fluent builder for handler registrations with per-handler middleware and the mediator middleware level | Configure mediator handler registrations |
| `MediatorHandlerBuilder.cs` | Per-handler middleware configuration with compile-time type safety | Configure middleware for individual handlers |

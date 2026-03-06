# DependencyInjection/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `MediatorEmitBuilderExtensions.cs` | AddMediator extension method registering mediator services and building typed dispatch pipelines | Register mediator or understand DI registration flow |
| `MediatorBuilder.cs` | Fluent builder for handler registrations with per-handler middleware and mediator-level inbound pipeline | Configure mediator handler registrations |
| `MediatorHandlerBuilder.cs` | Per-handler middleware configuration with compile-time type safety | Configure middleware for individual handlers |

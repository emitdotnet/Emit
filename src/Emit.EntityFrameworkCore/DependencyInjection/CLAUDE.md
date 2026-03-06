# DependencyInjection/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `EmitModelBuilder.cs` | Builder for database-specific model optimizations (UseNpgsql) | Add database provider support or modify model configuration |
| `EntityFrameworkCoreBuilder.cs` | Builder for configuring EF Core persistence with database provider selection | Extend EF Core registration API or add new providers |
| `EntityFrameworkCoreEmitBuilderExtensions.cs` | UseEntityFrameworkCore extension method registering repository and factory | Register EF Core persistence provider |
| `ModelBuilderExtensions.cs` | AddEmitModel extension on ModelBuilder for outbox and lock entities | Configure Emit entity model in user's DbContext |

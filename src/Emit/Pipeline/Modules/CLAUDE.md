# Modules/

The filter and validation middleware of consumer pipelines.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `FilterMiddleware.cs` | Runs registered consumer filters and short-circuits messages they reject | Understand consumer filter evaluation |
| `ValidationMiddleware.cs` | Validates messages before the handler and dead-letters or discards invalid ones inline | Understand how validators are resolved and how validation failures are handled |
